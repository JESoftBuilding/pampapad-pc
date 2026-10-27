import type { ReactNode } from "react";
import { cn } from "./utils";

/** Un campo de formulario: el rótulo, el control, una ayuda y, si lo hay, el error debajo, en el rojo. */
export function Field({ label, htmlFor, hint, error, className, children }: { label: string; htmlFor?: string; hint?: string; error?: string; className?: string; children: ReactNode }) {
  return (
    <div className={cn("space-y-1", className)}>
      <label htmlFor={htmlFor} className="block text-xs uppercase tracking-wide text-muted">
        {label}
      </label>
      {children}
      {hint !== undefined && error === undefined && <p className="text-xs text-muted">{hint}</p>}
      {error !== undefined && (
        <p role="alert" className="text-xs text-danger">
          {error}
        </p>
      )}
    </div>
  );
}
