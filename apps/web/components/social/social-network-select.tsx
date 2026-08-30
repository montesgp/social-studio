import type { SocialNetwork } from "@/lib/api/types";
import { Select, SelectProps } from "@/components/ui/select";

export interface SocialNetworkSelectProps
  extends Omit<SelectProps, "children" | "onChange"> {
  networks: SocialNetwork[];
  loading?: boolean;
  value: string;
  onChange: (value: string) => void;
  /** Label for the empty/placeholder option. Defaults to "Select network". */
  placeholder?: string;
}

/**
 * Presentational select for social networks.
 * Data fetching is the caller's responsibility — use the `useSocialNetworks`
 * hook and pass the resulting `networks` and `loading` props.
 */
export function SocialNetworkSelect({
  networks,
  loading = false,
  value,
  onChange,
  placeholder = "Select network",
  disabled,
  ...rest
}: SocialNetworkSelectProps) {
  return (
    <Select
      value={value}
      onChange={(e) => onChange(e.target.value)}
      disabled={disabled || loading}
      {...rest}
    >
      <option value="">{placeholder}</option>
      {networks.map((network) => (
        <option key={network.id} value={network.id}>
          {network.displayName}
        </option>
      ))}
    </Select>
  );
}
