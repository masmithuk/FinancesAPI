import type { FormEventHandler } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

interface NameCreateFormProps {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  placeholder: string;
  submitLabel: string;
  onSubmit: FormEventHandler<HTMLFormElement>;
  variant: "inline" | "card";
}

export default function NameCreateForm({
  id,
  label,
  value,
  onChange,
  placeholder,
  submitLabel,
  onSubmit,
  variant,
}: NameCreateFormProps) {
  return (
    <form className={`name-create-form name-create-form-${variant}`} onSubmit={onSubmit}>
      <div className="grid gap-1.5">
        <Label htmlFor={id}>{label}</Label>
        <Input
          id={id}
          required
          maxLength={100}
          value={value}
          onChange={(event) => onChange(event.target.value)}
          placeholder={placeholder}
        />
      </div>
      <Button className={variant === "card" ? "w-full" : ""} disabled={!value.trim()}>
        {submitLabel}
      </Button>
    </form>
  );
}
