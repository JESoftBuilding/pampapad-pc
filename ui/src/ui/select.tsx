import type { SelectHTMLAttributes } from "react";
import { cn } from "./utils";

/** La lista desplegable nativa, con la misma clase que la del Registro (LogView). Las opciones van como hijos. */
export function Select({ className, ...props }: SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <select
      className={cn(
        "h-9 w-full rounded-md border border-border bg-background px-2 text-sm text-foreground outline-none focus-visible:ring-2 focus-visible:ring-accent/60 disabled:opacity-50",
        className,
      )}
      {...props}
    />
  );
}
