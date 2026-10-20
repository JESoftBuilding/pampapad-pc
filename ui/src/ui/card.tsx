import type { HTMLAttributes } from "react";
import { cn } from "./utils";

/** Una tarjeta: cada zona de una pantalla va en una. */
export function Card({ className, ...props }: HTMLAttributes<HTMLElement>) {
  return <section className={cn("rounded-lg border border-border bg-card p-4", className)} {...props} />;
}

export function CardTitle({ className, ...props }: HTMLAttributes<HTMLHeadingElement>) {
  return <h2 className={cn("mb-3 font-display text-sm uppercase tracking-wide text-muted", className)} {...props} />;
}
