import { cn } from "./utils";

/** Un interruptor accesible (role switch), sin Radix: el wait de script.run, el negate de una condición. */
export function Switch({ checked, onChange, label, disabled = false }: { checked: boolean; onChange: (checked: boolean) => void; label: string; disabled?: boolean }) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={cn(
        "inline-flex h-6 w-11 shrink-0 items-center rounded-full border border-border transition-colors outline-none focus-visible:ring-2 focus-visible:ring-accent/60 disabled:opacity-50",
        checked ? "bg-accent" : "bg-background",
      )}
    >
      <span className={cn("block h-4 w-4 rounded-full bg-foreground transition-transform", checked ? "translate-x-6" : "translate-x-1")} />
    </button>
  );
}
