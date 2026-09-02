import { useEffect, useRef, useState } from "react";
import { getPalette, setPalette, type Palette } from "../../app/theme";

const PALETTES: ReadonlyArray<{ id: Palette; label: string; swatches: [string, string] }> = [
  { id: "indigo", label: "نیلی", swatches: ["#4f46e5", "#8b9dff"] },
  { id: "mint", label: "نعنایی", swatches: ["#0e7c6b", "#3ddc97"] },
  { id: "teal", label: "فیروزه‌ای", swatches: ["#0f766e", "#2dd4bf"] },
  { id: "zinc", label: "خاکستری", swatches: ["#2563eb", "#93b4ff"] },
];

export function ThemePalettePicker() {
  const [palette, setPaletteState] = useState(getPalette);
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    function onPointerDown(e: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) setOpen(false);
    }
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") setOpen(false);
    }
    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  const current = PALETTES.find((p) => p.id === palette) ?? PALETTES[0];

  return (
    <div ref={rootRef} className="relative flex-none">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        title="رنگ‌بندی"
        aria-label="رنگ‌بندی"
        className="grid h-8 w-8 place-items-center rounded-[9px] border border-(--edge) transition-colors hover:bg-(--hov)"
      >
        <span
          className="h-4 w-4 rounded-full border border-(--edge-2)"
          style={{ background: `linear-gradient(135deg, ${current.swatches[0]}, ${current.swatches[1]})` }}
        />
      </button>

      {open && (
        <div className="absolute end-0 top-9 z-50 w-40 rounded-[12px] border border-(--edge-2) bg-(--pane) p-1.5 shadow-lg">
          {PALETTES.map((p) => (
            <button
              key={p.id}
              type="button"
              onClick={() => {
                setPalette(p.id);
                setPaletteState(p.id);
                setOpen(false);
              }}
              className={`flex w-full items-center gap-2 rounded-[9px] px-2.5 py-2 text-right text-[12.5px] transition-colors hover:bg-(--hov) ${
                p.id === palette ? "border border-(--mint) bg-(--hov) text-(--ice)" : "text-(--ice-2)"
              }`}
            >
              <span
                className="h-3.5 w-3.5 flex-none rounded-full border border-(--edge-2)"
                style={{ background: `linear-gradient(135deg, ${p.swatches[0]}, ${p.swatches[1]})` }}
              />
              {p.label}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
