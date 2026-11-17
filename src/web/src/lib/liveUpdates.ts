import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { type QueryClient, useQueryClient } from '@tanstack/react-query'
import { useEffect } from 'react'
import { setLiveConnectionId } from '../api/http'

/** The server's hub (ChangesHub) and its one message, which says only that the company's data changed (SPEC 11.1). */
const HUB_PATH = '/hubs/changes'
const DATA_CHANGED = 'dataChanged'

/**
 * Answers that show the company's items, stock, orders and receipts. Other answers (the user, settings, the shop's
 * product preview, which asks the shop itself) don't change with that data, so a notice leaves them alone.
 */
const COMPANY_DATA = ['/api/items', '/api/orders', '/api/receipts', '/api/stock', '/api/dashboard', '/api/reports']

export function isCompanyDataQuery(queryKey: readonly unknown[]): boolean {
  const [path] = queryKey
  return typeof path === 'string' && COMPANY_DATA.some((prefix) => path === prefix || path.startsWith(`${prefix}/`))
}

/**
 * An import or a shop sync saves many changes in a row, so notices are gathered: the open screen loads again
 * once, this long after the first one.
 */
export const GATHER_MS = 1000

/** Waits between tries to reach the server; the last one repeats for as long as the page is open. */
export const RETRY_DELAYS_MS = [0, 2000, 5000, 10_000, 30_000]

export function retryDelay(previousTries: number): number {
  return RETRY_DELAYS_MS[Math.min(previousTries, RETRY_DELAYS_MS.length - 1)]
}

/** The part of SignalR's HubConnection used here, so tests can pass a stand-in. */
export interface ChangesConnection {
  /** Null while not connected. */
  readonly connectionId: string | null
  on(method: string, handler: () => void): void
  onreconnected(handler: () => void): void
  start(): Promise<void>
  stop(): Promise<void>
}

/**
 * Keeps `connection` open and reloads the company's data on the open screen when it changes. After a lost
 * connection everything shown is reloaded, so notices missed meanwhile don't matter. Returns a function that stops it.
 */
export function startLiveUpdates(queryClient: QueryClient, connection: ChangesConnection): () => void {
  let stopped = false
  let gathering: ReturnType<typeof setTimeout> | undefined
  let retrying: ReturnType<typeof setTimeout> | undefined

  const reload = () => {
    gathering = undefined
    void queryClient.invalidateQueries({ predicate: (query) => isCompanyDataQuery(query.queryKey) })
  }

  connection.on(DATA_CHANGED, () => {
    gathering ??= setTimeout(reload, GATHER_MS)
  })
  connection.onreconnected(reload)
  // This tab's own changes are reloaded by the tab itself, so the server leaves it out of their notice.
  const connectionId = () => connection.connectionId
  setLiveConnectionId(connectionId)

  // SignalR reconnects by itself only once it was connected; the first connection is retried here.
  const connect = (tries: number) => {
    connection.start().then(
      () => {
        if (tries > 0 && !stopped) reload()
      },
      () => {
        if (!stopped) retrying = setTimeout(() => connect(tries + 1), retryDelay(tries + 1))
      },
    )
  }
  connect(0)

  return () => {
    stopped = true
    setLiveConnectionId(() => null)
    clearTimeout(gathering)
    clearTimeout(retrying)
    void connection.stop()
  }
}

/** Reloads what the open screen shows when someone else (or the shop) changes the company's data. */
export function useLiveUpdates() {
  const queryClient = useQueryClient()

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(HUB_PATH)
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: ({ previousRetryCount }) => retryDelay(previousRetryCount) })
      // A dropped connection is expected (deploys, sleep, a phone in a pocket) and is retried, so it isn't an error.
      .configureLogging(import.meta.env.DEV ? LogLevel.Warning : LogLevel.None)
      .build()
    return startLiveUpdates(queryClient, connection)
  }, [queryClient])
}
