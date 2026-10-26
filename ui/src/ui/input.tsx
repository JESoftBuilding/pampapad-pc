import type { InputHTMLAttributes } from "react";
import { cn } from "./utils";

/** La entrada de texto o de número, con el patrón de shadcn y sin librería. El aro de foco es el ámbar de lo elegido (decisión 28). */
export function Input({ className, type = "text", ...props }: InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      type={type}
      className={cn(
        "h-9 w-full rounded-md border border-border bg-background px-3 text-sm text-foreground outline-none placeholder:text-muted/70 focus-visible:ring-2 focus-visible:ring-accent/60 disabled:opacity-50",
        className,
      )}
      {...props}
    />
  );
}
