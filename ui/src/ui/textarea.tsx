import type { TextareaHTMLAttributes } from "react";
import { cn } from "./utils";

/** El área de texto: para el JSON de obs.request y para mostrar un paso de tipo desconocido tal cual. */
export function Textarea({ className, ...props }: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return (
    <textarea
      className={cn(
        "min-h-24 w-full rounded-md border border-border bg-background px-3 py-2 font-mono text-xs text-foreground outline-none focus-visible:ring-2 focus-visible:ring-accent/60 disabled:opacity-50",
        className,
      )}
      {...props}
    />
  );
}
