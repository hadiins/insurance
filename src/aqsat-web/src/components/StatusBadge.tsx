type Tone = "mint" | "moss" | "ember" | "amber" | "neutral";

const TONE_LIGHT: Record<Tone, string> = {
  mint: "bg-(--mint)/12 text-(--mint)",
  moss: "bg-(--moss)/12 text-(--moss)",
  ember: "bg-(--ember)/13 text-(--ember)",
  amber: "bg-(--amber)/13 text-(--amber)",
  neutral: "bg-(--ice-3)/12 text-(--ice-3)",
};

const TONE_SOLID: Record<Tone, string> = {
  mint: "bg-(--mint) text-(--on-mint)",
  moss: "bg-(--moss) text-(--on-mint)",
  ember: "bg-(--ember) text-white",
  amber: "bg-(--amber) text-white",
  neutral: "bg-(--ice-3) text-white",
};

/** Shared status pill (TailAdmin's Badge pattern, on our design tokens) — replaces the
 * hand-written `rounded-full bg-(--mint)/12 text-(--mint)` repetition across pages. */
export function StatusBadge({
  tone = "neutral",
  solid = false,
  className = "",
  children,
}: {
  tone?: Tone;
  solid?: boolean;
  className?: string;
  children: React.ReactNode;
}) {
  return (
    <span
      className={`inline-flex items-center justify-center gap-1 rounded-full px-2.5 py-0.5 text-[11.5px] font-semibold ${solid ? TONE_SOLID[tone] : TONE_LIGHT[tone]} ${className}`}
    >
      {children}
    </span>
  );
}
