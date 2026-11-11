import { describeApiError } from "../api/errorMessages";

/**
 * What a failed request says, drawn the same way on every screen: the backend's
 * own message as written, or the generic fallback line followed by a short detail.
 * The text comes from `describeApiError`; this only draws it.
 *
 * `detailClassName` lets a screen style the detail line (the default is the
 * shared state-card one).
 */
export default function ApiErrorText({
  error,
  detailClassName = "state-card-detail",
}: {
  error: unknown;
  detailClassName?: string;
}) {
  const { message, detail, fromBackend } = describeApiError(error);
  if (fromBackend) {
    return <span>{message}</span>;
  }
  return (
    <>
      <strong>{message}</strong>
      {detail !== null && <span className={detailClassName}>{detail}</span>}
    </>
  );
}
