using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Zalihe.Application.Channels;
using Zalihe.Application.Orders;
using Zalihe.Infrastructure.Persistence;
using Zalihe.Infrastructure.Tenancy;
using Zalihe.IntegrationTests.Infrastructure;

namespace Zalihe.IntegrationTests.Realtime;

[Collection(ApiCollection.Name)]
public class ChangesHubTests(ZaliheApiFactory factory)
{
    private static readonly TimeSpan Arrives = TimeSpan.FromSeconds(5);
    // A notice is sent before the request that caused it answers, so a short wait is enough to see none came.
    private static readonly TimeSpan Silence = TimeSpan.FromMilliseconds(500);

    private static async Task<Guid> CreateItemAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/items", new { name = "Kafa Etiopija", sku = "KF-ETI-250", unit = "kom", minStock = 0 });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Connect_SignedOut_IsRefused()
    {
        // Act & Assert
        await Should.ThrowAsync<Exception>(() => HubListener.ConnectAsync(factory, cookies: null));
    }

    [Fact]
    public async Task RecordMovement_OwnCompany_GetsDataChangedAndOtherCompanyNothing()
    {
        // Arrange
        var (clientA, cookiesA) = await factory.CreateSignedInBrowserAsync();
        var (_, cookiesB) = await factory.CreateSignedInBrowserAsync();
        var itemId = await CreateItemAsync(clientA);
        await using var listenerA = await HubListener.ConnectAsync(factory, cookiesA);
        await using var listenerB = await HubListener.ConnectAsync(factory, cookiesB);

        // Act
        (await clientA.PostAsJsonAsync($"/api/items/{itemId}/movements", new { kind = "receipt", quantity = 5 }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // Assert
        (await listenerA.NextMessageAsync(Arrives)).ShouldBe("dataChanged");
        (await listenerB.NextMessageAsync(Silence)).ShouldBeNull();
    }

    [Fact]
    public async Task RecordMovement_FromOneTab_SkipsThatTabButNotTheOthers()
    {
        // Arrange: two tabs of the same browser; the first one makes the change and reloads by itself.
        var (client, cookies) = await factory.CreateSignedInBrowserAsync();
        var itemId = await CreateItemAsync(client);
        await using var changingTab = await HubListener.ConnectAsync(factory, cookies);
        await using var otherTab = await HubListener.ConnectAsync(factory, cookies);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/items/{itemId}/movements")
        {
            Content = JsonContent.Create(new { kind = "receipt", quantity = 5 }),
        };
        request.Headers.Add("X-Connection-Id", changingTab.ConnectionId);

        // Act
        (await client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.Created);

        // Assert
        (await otherTab.NextMessageAsync(Arrives)).ShouldBe("dataChanged");
        (await changingTab.NextMessageAsync(Silence)).ShouldBeNull();
    }

    [Fact]
    public async Task ProcessShopOrder_InBackground_GetsDataChanged()
    {
        // Arrange: shop orders are processed by background jobs, outside any request.
        var (client, cookies) = await factory.CreateSignedInBrowserAsync();
        var (_, channelId) = await LinkedShop.CreateAsync(factory, client);
        await using var listener = await HubListener.ConnectAsync(factory, cookies);
        using var scope = factory.Services.CreateScope();
        var tenantId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().SalesChannels
            .IgnoreQueryFilters().Where(c => c.Id == channelId).Select(c => c.TenantId).SingleAsync();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenantId);
        var order = new ShopOrder("1041", "processing", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [new ShopOrderLine("10", 2)]);

        // Act
        await scope.ServiceProvider.GetRequiredService<OrderService>().ProcessAsync(channelId, order, CancellationToken.None);

        // Assert
        (await listener.NextMessageAsync(Arrives)).ShouldBe("dataChanged");
        (await LinkedShop.StockAsync(client, "KF-ETI-250")).ShouldBe(22);
    }

    [Fact]
    public async Task SaveInTransaction_AnnouncedOnlyAfterCommit()
    {
        // Arrange
        var (client, cookies) = await factory.CreateSignedInBrowserAsync();
        var itemId = await CreateItemAsync(client);
        await using var listener = await HubListener.ConnectAsync(factory, cookies);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.Items.IgnoreQueryFilters().SingleAsync(i => i.Id == itemId);
        await using var transaction = await db.Database.BeginTransactionAsync();

        // Act
        db.Entry(item).Property(i => i.Name).CurrentValue = "Kafa Kenija";
        await db.SaveChangesAsync();
        var beforeCommit = await listener.NextMessageAsync(Silence);
        await transaction.CommitAsync();

        // Assert
        beforeCommit.ShouldBeNull();
        (await listener.NextMessageAsync(Arrives)).ShouldBe("dataChanged");
    }

    [Fact]
    public async Task SaveInTransaction_RolledBack_IsNotAnnounced()
    {
        // Arrange
        var (client, cookies) = await factory.CreateSignedInBrowserAsync();
        var itemId = await CreateItemAsync(client);
        await using var listener = await HubListener.ConnectAsync(factory, cookies);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.Items.IgnoreQueryFilters().SingleAsync(i => i.Id == itemId);

        // Act
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            db.Entry(item).Property(i => i.Name).CurrentValue = "Kafa Kenija";
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        // Assert
        (await listener.NextMessageAsync(Silence)).ShouldBeNull();
    }

    [Fact]
    public async Task ChangeOutsideScreenData_IsNotAnnounced()
    {
        // Arrange: a company setting is not item, stock, order or receipt data.
        var (client, cookies) = await factory.CreateSignedInBrowserAsync();
        await using var listener = await HubListener.ConnectAsync(factory, cookies);

        // Act
        (await client.PutAsJsonAsync("/api/account/company", new { name = "Nova Firma" })).EnsureSuccessStatusCode();

        // Assert
        (await listener.NextMessageAsync(Silence)).ShouldBeNull();
    }
}
