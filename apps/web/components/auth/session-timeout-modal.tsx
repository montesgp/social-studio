"use client";

import { useEffect, useState } from "react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";

interface SessionTimeoutModalProps {
  open: boolean;
  countdownSeconds: number;
  onStay: () => void;
  onSignOut: () => void;
}

export function SessionTimeoutModal({ open, countdownSeconds, onStay, onSignOut }: SessionTimeoutModalProps) {
  const [remaining, setRemaining] = useState(countdownSeconds);

  useEffect(() => {
    if (!open) {
      setRemaining(countdownSeconds);
      return;
    }

    setRemaining(countdownSeconds);
    const interval = setInterval(() => {
      setRemaining((prev) => {
        if (prev <= 1) {
          clearInterval(interval);
          return 0;
        }
        return prev - 1;
      });
    }, 1000);

    return () => clearInterval(interval);
  }, [open, countdownSeconds]);

  return (
    <Dialog open={open} onOpenChange={(isOpen) => { if (!isOpen) onStay(); }}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Session about to expire</DialogTitle>
          <DialogDescription>
            Your session will expire due to inactivity in{" "}
            <span className="font-semibold text-foreground">{remaining}s</span>.
            Do you want to stay signed in?
          </DialogDescription>
        </DialogHeader>
        <DialogFooter className="flex-row gap-2 sm:justify-end">
          <Button variant="outline" onClick={onSignOut}>
            Sign out
          </Button>
          <Button onClick={onStay}>Stay signed in</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
