import type { CSSProperties } from "react";
import type { SchoolClass } from "../api/types";
import { SEAT_SEGMENTS, seatSegments } from "./classSeats";

/**
 * A row of small "seats" that pictures a class's numbers: dark for confirmed,
 * striped gold for held, pale for free. It is only a picture (aria-hidden); the
 * same numbers are written out next to it, so nothing depends on seeing it.
 * Segment colours change with a transition, so a seat being taken or released
 * is visible as it happens.
 */
export default function SeatStrip({ schoolClass }: { schoolClass: SchoolClass }) {
  const { confirmed, held } = seatSegments(
    schoolClass.totalSeats,
    schoolClass.heldSeats,
    schoolClass.confirmedSeats,
  );

  return (
    <div className="seat-strip" aria-hidden="true">
      {Array.from({ length: SEAT_SEGMENTS }, (_, n) => {
        const kind = n < confirmed ? "confirmed" : n < confirmed + held ? "held" : "free";
        return (
          <span
            key={n}
            className={`seat seat--${kind}`}
            style={{ "--n": n } as CSSProperties}
          />
        );
      })}
    </div>
  );
}
