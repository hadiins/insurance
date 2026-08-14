export function BlankPage({ title }: { title: string }) {
  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">{title}</h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">این صفحه در نسخهٔ فعلی ساخته نشده</div>
      <div className="rounded-2xl border border-(--edge) bg-(--pane) p-8 text-center text-[12.5px] text-(--ice-3)">
        محتوای «{title}»
      </div>
    </div>
  );
}
