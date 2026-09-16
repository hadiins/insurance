import { WarningOctagonIcon } from "@phosphor-icons/react";

/** The error leg of the loading/empty/error triad (CLAUDE.md rule 16). A failure must never
 * look like "no data", so this states what failed, in Persian, and offers the retry — the
 * technical detail stays in the log.
 *
 * The mark is deliberately louder than EmptyState's: a filled octagon in the danger tone,
 * so a failure never reads as an ordinary empty list at a glance. */
export function ErrorState({
  title = "بارگذاری اطلاعات ناموفق بود",
  description,
  onRetry,
}: {
  title?: string;
  description?: string;
  onRetry?: () => void;
}) {
  return (
    <div className="rounded-(--r-lg) border border-(--ember)/30 bg-(--ember)/6 px-4 py-10 text-center">
      <WarningOctagonIcon size={30} weight="fill" className="mx-auto mb-3 text-(--ember)" />
      <div className="mb-1.5 text-[13.5px] font-semibold text-(--ember)">{title}</div>
      {description && (
        <div className="mx-auto mb-5 max-w-md text-[12.5px] leading-relaxed text-(--ice-3)">{description}</div>
      )}
      {onRetry && (
        <button
          type="button"
          onClick={onRetry}
          className="rounded-(--r) border border-(--ember)/45 px-3.5 py-1.5 text-[12.5px] font-semibold text-(--ember) transition-colors hover:bg-(--ember)/15"
        >
          تلاش دوباره
        </button>
      )}
    </div>
  );
}
