import { useId, type InputHTMLAttributes, type ReactNode } from "react";
import { Button as ShadcnButton } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { ComponentProps } from "react";

type ButtonVariant = "primary" | "dark" | "outline" | "danger" | "ghost" | "link";

export function Button({
  variant,
  size,
  wide = false,
  className,
  children,
  ...props
}: Omit<ComponentProps<typeof ShadcnButton>, "variant" | "size"> & {
  variant: ButtonVariant;
  size?: "small";
  wide?: boolean;
}) {
  const shadcnVariant =
    variant === "outline"
      ? "outline"
      : variant === "danger"
        ? "destructive"
        : variant === "ghost" || variant === "link"
          ? variant
          : "default";

  return (
    <ShadcnButton
      variant={shadcnVariant}
      size={size === "small" ? "sm" : "default"}
      className={[wide ? "w-full" : "", className].filter(Boolean).join(" ")}
      {...props}
    >
      {children}
    </ShadcnButton>
  );
}

export function TextField({
  label,
  value,
  onChange,
  className = "",
  ...props
}: Omit<InputHTMLAttributes<HTMLInputElement>, "onChange" | "value"> & {
  label: ReactNode;
  value: string;
  onChange: (value: string) => void;
  className?: string;
}) {
  const id = useId();

  return (
    <div className={`grid gap-1.5 ${className}`}>
      <Label htmlFor={id}>{label}</Label>
      <Input id={id} value={value} onChange={(event) => onChange(event.target.value)} {...props} />
    </div>
  );
}

export function SelectField({
  label,
  options,
  value,
  onChange,
  id,
}: {
  label: ReactNode;
  options: Array<{ value: string; label: string }>;
  value: string;
  onChange: (value: string) => void;
  id?: string;
}) {
  const generatedId = useId();

  return (
    <div className="grid gap-1.5">
      <Label htmlFor={id ?? generatedId}>{label}</Label>
      <select
        id={id ?? generatedId}
        className="h-9 w-full rounded-lg border border-input bg-background px-3 text-sm shadow-xs outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50"
        value={value}
        onChange={(event) => onChange(event.target.value)}
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </div>
  );
}

export function ToggleField({
  label,
  checked,
  onChange,
}: {
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
}) {
  const id = useId();

  return (
    <div className="flex items-center gap-2">
      <input
        id={id}
        type="checkbox"
        role="switch"
        className="peer sr-only"
        checked={checked}
        onChange={(event) => onChange(event.target.checked)}
      />
      <span
        aria-hidden="true"
        className="relative inline-flex h-[18px] w-8 shrink-0 cursor-pointer rounded-full bg-input transition-colors before:absolute before:top-px before:left-px before:size-4 before:rounded-full before:bg-background before:transition-transform before:content-[''] peer-checked:bg-primary peer-checked:before:translate-x-[13px] peer-focus-visible:ring-3 peer-focus-visible:ring-ring/50"
      />
      <Label htmlFor={id}>{label}</Label>
    </div>
  );
}
