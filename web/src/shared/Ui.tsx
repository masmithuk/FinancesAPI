import type { ReactNode } from "react";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";

export function PageHeading({
  eyebrow,
  title,
  subtitle,
  action,
}: {
  eyebrow: string;
  title: string;
  subtitle: string;
  action?: ReactNode;
}) {
  return (
    <div className="page-heading">
      <div>
        <p className="eyebrow">{eyebrow}</p>
        <h1>{title}</h1>
        <p className="muted">{subtitle}</p>
      </div>
      {action}
    </div>
  );
}

export function Panel({
  title,
  subtitle,
  children,
  className = "",
}: {
  title: string;
  subtitle?: string;
  children: ReactNode;
  className?: string;
}) {
  return (
    <Card className={`panel ${className}`}>
      <CardHeader className="panel-heading">
        <div>
          <CardTitle role="heading" aria-level={2}>
            {title}
          </CardTitle>
          {subtitle && <CardDescription>{subtitle}</CardDescription>}
        </div>
      </CardHeader>
      <CardContent>{children}</CardContent>
    </Card>
  );
}

export function MetricCard({
  icon,
  tone,
  label,
  value,
  description,
  actionLabel,
  onAction,
}: {
  icon: string;
  tone: "sage" | "peach";
  label: string;
  value: number;
  description: string;
  actionLabel: string;
  onAction: () => void;
}) {
  return (
    <Card className="metric-card">
      <span className={`metric-icon ${tone}`}>{icon}</span>
      <span className="metric-label">{label}</span>
      <strong>{value}</strong>
      <p>{description}</p>
      <Button variant="link" className="subtle-link" onClick={onAction}>
        {actionLabel} <span>→</span>
      </Button>
    </Card>
  );
}

export function ErrorMessage({ message }: { message: string }) {
  return message ? (
    <Alert variant="destructive" className="notice notice-error">
      <AlertDescription>{message}</AlertDescription>
    </Alert>
  ) : null;
}

export function EmptyState({ title, text }: { title: string; text: string }) {
  return (
    <div className="empty-state">
      <span className="empty-mark">✳</span>
      <strong>{title}</strong>
      <p>{text}</p>
    </div>
  );
}
