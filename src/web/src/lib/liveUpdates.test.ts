import { QueryClient } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { customFetch } from '../api/http'
import { type ChangesConnection, GATHER_MS, isCompanyDataQuery, retryDelay, startLiveUpdates } from './liveUpdates'

/** Stands in for the SignalR connection: the test sends the server's notices and decides whether starting works. */
function fakeConnection(startResults: boolean[] = [true]) {
  const handlers = new Map<string, () => void>()
  let reconnected = () => {}
  const connection: ChangesConnection & { notify(): void; reconnect(): void; starts: number } = {
    connectionId: 'tab-1',
    starts: 0,
    on: (method, handler) => handlers.set(method, handler),
    onreconnected: (handler) => (reconnected = handler),
    start: () => {
      const ok = startResults[connection.starts] ?? true
      connection.starts++
      return ok ? Promise.resolve() : Promise.reject(new Error('offline'))
    },
    stop: () => Promise.resolve(),
    notify: () => handlers.get('dataChanged')?.(),
    reconnect: () => reconnected(),
  }
  return connection
}

describe('isCompanyDataQuery', () => {
  it.each([
    ['/api/items', true],
    ['/api/items/123/stock-history', true],
    ['/api/orders', true],
    ['/api/receipts/1', true],
    ['/api/stock/summary', true],
    ['/api/dashboard', true],
    ['/api/reports/top-sellers', true],
    ['/api/auth/me', false],
    ['/api/account/users', false],
    ['/api/channels/1/products/preview', false],
    ['/api/itemsx', false],
  ])('%s → %s', (path, expected) => {
    expect(isCompanyDataQuery([path, { page: 1 }])).toBe(expected)
  })

  it('ignores keys that are not addresses', () => {
    expect(isCompanyDataQuery([42])).toBe(false)
  })
})

describe('startLiveUpdates', () => {
  let queryClient: QueryClient

  beforeEach(() => {
    vi.useFakeTimers()
    queryClient = new QueryClient()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('reloads company data once for notices that arrive together', async () => {
    // Arrange
    queryClient.setQueryData(['/api/items'], [])
    queryClient.setQueryData(['/api/auth/me'], {})
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries')
    const connection = fakeConnection()
    startLiveUpdates(queryClient, connection)

    // Act
    connection.notify()
    connection.notify()
    connection.notify()
    await vi.advanceTimersByTimeAsync(GATHER_MS)

    // Assert
    expect(invalidate).toHaveBeenCalledTimes(1)
    expect(queryClient.getQueryState(['/api/items'])?.isInvalidated).toBe(true)
    expect(queryClient.getQueryState(['/api/auth/me'])?.isInvalidated).toBe(false)
  })

  it('reloads after a lost connection is back', () => {
    // Arrange
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries')
    const connection = fakeConnection()
    startLiveUpdates(queryClient, connection)

    // Act
    connection.reconnect()

    // Assert
    expect(invalidate).toHaveBeenCalledTimes(1)
  })

  it('keeps trying to connect and reloads once it does', async () => {
    // Arrange
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries')
    const connection = fakeConnection([false, false, true])

    // Act
    startLiveUpdates(queryClient, connection)
    await vi.advanceTimersByTimeAsync(retryDelay(1) + retryDelay(2))

    // Assert
    expect(connection.starts).toBe(3)
    expect(invalidate).toHaveBeenCalledTimes(1)
  })

  it('does nothing more once stopped', async () => {
    // Arrange
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries')
    const connection = fakeConnection([false])
    const stop = startLiveUpdates(queryClient, connection)
    connection.notify()

    // Act
    stop()
    await vi.advanceTimersByTimeAsync(60_000)

    // Assert
    expect(connection.starts).toBe(1)
    expect(invalidate).not.toHaveBeenCalled()
  })

  it('names its connection in changes, but not in reads or after stopping', async () => {
    // Arrange
    const fetch = vi.spyOn(globalThis, 'fetch').mockImplementation(() => Promise.resolve(new Response(null, { status: 204 })))
    const sent = (call: number) => new Headers(fetch.mock.calls[call][1]?.headers).get('X-Connection-Id')
    const stop = startLiveUpdates(queryClient, fakeConnection())

    // Act
    await customFetch('/api/items', { method: 'POST' })
    await customFetch('/api/items')
    stop()
    await customFetch('/api/items', { method: 'POST' })

    // Assert
    expect(sent(0)).toBe('tab-1')
    expect(sent(1)).toBeNull()
    expect(sent(2)).toBeNull()
  })
})
