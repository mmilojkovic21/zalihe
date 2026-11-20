import { create } from 'zustand'

// UI state shared by the whole app, so any screen can open these without passing callbacks down.
// Data from the server stays in TanStack Query.
interface UiState {
  quickSearchOpen: boolean
  feedbackOpen: boolean
  openQuickSearch: () => void
  closeQuickSearch: () => void
  openFeedback: () => void
  closeFeedback: () => void
}

const initialState = { quickSearchOpen: false, feedbackOpen: false }

export const useUiStore = create<UiState>()((set) => ({
  ...initialState,
  // Only one overlay at a time: opening one closes the other.
  openQuickSearch: () => set({ quickSearchOpen: true, feedbackOpen: false }),
  closeQuickSearch: () => set({ quickSearchOpen: false }),
  openFeedback: () => set({ feedbackOpen: true, quickSearchOpen: false }),
  closeFeedback: () => set({ feedbackOpen: false }),
}))

// Back to the initial state; tests call it so an open modal doesn't leak into the next test.
export function resetUiStore() {
  useUiStore.setState(initialState)
}
