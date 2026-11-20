import '@testing-library/jest-dom/vitest'
import { cleanup, configure } from '@testing-library/react'
import { afterEach, vi } from 'vitest'
import '../i18n'
import { resetUiStore } from '../stores/ui'

// findBy… and waitFor give up after 1 s by default, too soon when the machine is busy.
configure({ asyncUtilTimeout: 3000 })

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  resetUiStore()
})

// Browser APIs Mantine relies on that jsdom doesn't implement.
Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  }),
})

window.ResizeObserver = class {
  observe() {}
  unobserve() {}
  disconnect() {}
}

Element.prototype.scrollIntoView = () => {}
