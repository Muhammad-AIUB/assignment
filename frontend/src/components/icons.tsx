// Small inline icons and the brand mark. All are decorative (aria-hidden): the
// text next to them carries the meaning. They draw in `currentColor`, so the
// surrounding CSS decides their colour.

import type { SVGProps } from "react";

type IconProps = SVGProps<SVGSVGElement>;

const base: IconProps = {
  viewBox: "0 0 24 24",
  fill: "none",
  stroke: "currentColor",
  strokeWidth: 2,
  strokeLinecap: "round",
  strokeLinejoin: "round",
  "aria-hidden": true,
  focusable: false,
};

export function ArrowRightIcon(props: IconProps) {
  return (
    <svg {...base} {...props}>
      <path d="M5 12h14M13 6l6 6-6 6" />
    </svg>
  );
}

export function IdCardIcon(props: IconProps) {
  return (
    <svg {...base} {...props}>
      <rect x="3" y="5" width="18" height="14" rx="3" />
      <circle cx="9" cy="11" r="2" />
      <path d="M6.5 16c.6-1.4 1.6-2 2.5-2s1.9.6 2.5 2M14 10h4M14 14h3" />
    </svg>
  );
}

export function CheckIcon(props: IconProps) {
  return (
    <svg {...base} {...props}>
      <path d="M5 12.5l4.5 4.5L19 7.5" />
    </svg>
  );
}

export function AlertIcon(props: IconProps) {
  return (
    <svg {...base} {...props}>
      <path d="M12 4l9 16H3L12 4z" />
      <path d="M12 10v4M12 17.2v.1" />
    </svg>
  );
}

export function InboxIcon(props: IconProps) {
  return (
    <svg {...base} {...props}>
      <path d="M4 13l2.4-7A2 2 0 0 1 8.3 4.7h7.4a2 2 0 0 1 1.9 1.3L20 13" />
      <path d="M4 13v4a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-4h-4.5a2 2 0 0 1-1.8 1.1h-3.4A2 2 0 0 1 8.5 13H4z" />
    </svg>
  );
}

/** The mark: a doorway with a light in it. Matches public/favicon.svg. */
export function BrandMark(props: IconProps) {
  return (
    <svg viewBox="0 0 32 32" aria-hidden="true" focusable={false} {...props}>
      <rect width="32" height="32" rx="9" fill="#f4efe4" />
      <path d="M8.5 25V15.5a7.5 7.5 0 0 1 15 0V25z" fill="#0a3326" />
      <circle cx="16" cy="15.5" r="3" fill="#d2381f" />
    </svg>
  );
}
