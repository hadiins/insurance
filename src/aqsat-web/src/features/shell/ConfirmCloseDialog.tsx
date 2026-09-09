import * as Dialog from "@radix-ui/react-dialog";
import { useTabsStore } from "../../app/store/tabsStore";

export function ConfirmCloseDialog() {
  const pendingCloseKey = useTabsStore((s) => s.pendingCloseKey);
  const confirmClose = useTabsStore((s) => s.confirmClose);
  const cancelClose = useTabsStore((s) => s.cancelClose);

  return (
    <Dialog.Root open={pendingCloseKey !== null} onOpenChange={(open) => !open && cancelClose()}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-[60] bg-black/55" />
        <Dialog.Content className="fixed inset-0 z-[60] grid place-items-center p-5">
          <div className="max-w-[400px] rounded-2xl border border-(--edge-2) bg-(--slate) p-5.5 shadow-[var(--sh)]">
            <Dialog.Title className="mb-1.5 text-[15px] font-bold text-(--ice)">
              کار ذخیره‌نشده دارید
            </Dialog.Title>
            <Dialog.Description className="mb-4.5 text-[13.5px] text-(--ice-2)">
              این تب تغییرات ذخیره‌نشده دارد. اگر ببندید، اطلاعات از دست می‌رود.
            </Dialog.Description>
            <div className="flex gap-2">
              <button
                type="button"
                onClick={cancelClose}
                className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) shadow-[var(--gl-mint)] transition-colors hover:brightness-105"
              >
                برگرد و ذخیره کن
              </button>
              <button
                type="button"
                onClick={confirmClose}
                className="rounded-[10px] border border-(--edge-2) bg-(--btn-bg) px-4 py-2 text-[12.5px] font-semibold text-(--ice-2) transition-colors hover:bg-(--btn-hov) hover:text-(--ice)"
              >
                بستن بدون ذخیره
              </button>
            </div>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
