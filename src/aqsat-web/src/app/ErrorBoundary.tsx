import { Component, type ErrorInfo, type ReactNode } from "react";

interface Props {
  children: ReactNode;
}

interface State {
  error: Error | null;
}

/// docs/TASKS.md Task 19's check: kill the database mid-session, no blank screen anywhere. A failed
/// fetch inside a page is caught locally (every list already renders loading/empty/error), but a
/// render-time exception anywhere in the tree has no such guard — without this boundary, React
/// unmounts the whole app and leaves a blank white screen. This is the outermost safety net, not a
/// substitute for per-page error handling.
export class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // eslint-disable-next-line no-console
    console.error("Unhandled render error", error, info.componentStack);
  }

  render() {
    if (this.state.error) {
      return (
        <div className="grid h-full place-items-center bg-(--void) px-6 text-center">
          <div>
            <div className="mb-2 text-lg font-extrabold text-(--ice)">خطایی پیش‌بینی‌نشده رخ داد</div>
            <div className="mb-5 text-[13px] text-(--ice-3)">
              اتصال به سرور یا پایگاه‌داده ممکن است قطع شده باشد. اطلاعات واردشدهٔ شما حفظ مانده است.
            </div>
            <button
              type="button"
              onClick={() => window.location.reload()}
              className="rounded-[10px] border border-(--mint) bg-(--mint) px-5 py-2.5 text-[13px] font-semibold text-(--on-mint) transition-colors hover:brightness-105"
            >
              تلاش دوباره
            </button>
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}
