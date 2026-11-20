import { describe, expect, it } from 'vitest'
import { useUiStore } from './ui'

describe('useUiStore', () => {
  it('openFeedback_WhenQuickSearchIsOpen_ClosesQuickSearch', () => {
    // Arrange
    useUiStore.getState().openQuickSearch()

    // Act
    useUiStore.getState().openFeedback()

    // Assert
    const state = useUiStore.getState()
    expect(state.feedbackOpen).toBe(true)
    expect(state.quickSearchOpen).toBe(false)
  })

  it('openQuickSearch_WhenFeedbackIsOpen_ClosesFeedback', () => {
    // Arrange
    useUiStore.getState().openFeedback()

    // Act
    useUiStore.getState().openQuickSearch()

    // Assert
    const state = useUiStore.getState()
    expect(state.quickSearchOpen).toBe(true)
    expect(state.feedbackOpen).toBe(false)
  })

  it('closeFeedback_WhenOpen_ClosesOnlyFeedback', () => {
    // Arrange
    useUiStore.setState({ quickSearchOpen: true, feedbackOpen: true })

    // Act
    useUiStore.getState().closeFeedback()

    // Assert
    const state = useUiStore.getState()
    expect(state.feedbackOpen).toBe(false)
    expect(state.quickSearchOpen).toBe(true)
  })
})
