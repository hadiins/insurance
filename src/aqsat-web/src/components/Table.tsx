/** Shared table primitives — the exact classes every list page currently hand-writes, extracted
 * once so a spacing or border tweak lands everywhere at the same time. */
/** `plain` drops the card chrome (border, radius, background) for a table that sits *inside* a
 * panel which already draws it — otherwise the two frames nest and read as a box in a box. */
export function Table({
  children,
  className = "",
  plain = false,
}: {
  children: React.ReactNode;
  className?: string;
  plain?: boolean;
}) {
  const chrome = plain ? "" : "overflow-hidden rounded-(--r-lg) border border-(--edge) bg-(--pane)";
  return (
    <div className={`${chrome} ${className}`}>
      <table className="w-full border-collapse">{children}</table>
    </div>
  );
}

export function Th({ children, className = "" }: { children?: React.ReactNode; className?: string }) {
  return (
    <th className={`border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3) ${className}`}>
      {children}
    </th>
  );
}

export function Td({
  children,
  className = "",
  ltr = false,
  colSpan,
  onClick,
}: {
  children?: React.ReactNode;
  className?: string;
  ltr?: boolean;
  colSpan?: number;
  onClick?: React.MouseEventHandler<HTMLTableCellElement>;
}) {
  return (
    <td
      colSpan={colSpan}
      onClick={onClick}
      dir={ltr ? "ltr" : undefined}
      className={`px-3 py-2 text-[13.5px] ${className}`}
    >
      {children}
    </td>
  );
}

export function Tr({
  children,
  onClick,
  className = "",
}: {
  children: React.ReactNode;
  onClick?: () => void;
  className?: string;
}) {
  return (
    <tr
      onClick={onClick}
      className={`border-t border-(--edge) first:border-t-0 ${onClick ? "cursor-pointer transition-colors hover:bg-(--hov)" : ""} ${className}`}
    >
      {children}
    </tr>
  );
}
