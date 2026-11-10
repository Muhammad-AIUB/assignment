import { describe, expect, it } from "vitest";
import {
  getStudentId,
  isValidStudentId,
  resetStudentIdStoreForTests,
  setStudentId,
  STUDENT_ID_STORAGE_KEY,
} from "./studentIdStore";

const VALID_ID = "20152691234567890";

describe("student id store", () => {
  it("accepts exactly 17 digits and nothing else", () => {
    expect(isValidStudentId(VALID_ID)).toBe(true);
    expect(isValidStudentId("2015269123456789")).toBe(false); // 16
    expect(isValidStudentId("201526912345678901")).toBe(false); // 18
    expect(isValidStudentId("2015269123456789a")).toBe(false);
    expect(isValidStudentId(`${VALID_ID}\n`)).toBe(false);
  });

  it("saves an accepted id in localStorage under the key studentId", () => {
    expect(setStudentId(VALID_ID)).toBe(true);

    expect(window.localStorage.getItem(STUDENT_ID_STORAGE_KEY)).toBe(VALID_ID);
    expect(getStudentId()).toBe(VALID_ID);
  });

  it("rejects an invalid id without changing the saved one", () => {
    setStudentId(VALID_ID);

    expect(setStudentId("123")).toBe(false);
    expect(getStudentId()).toBe(VALID_ID);
  });

  it("reads the id a returning student saved on an earlier visit", () => {
    window.localStorage.setItem(STUDENT_ID_STORAGE_KEY, VALID_ID);
    resetStudentIdStoreForTests();

    expect(getStudentId()).toBe(VALID_ID);
  });

  it("ignores a stored value that is not a valid id", () => {
    window.localStorage.setItem(STUDENT_ID_STORAGE_KEY, "not-an-id");
    resetStudentIdStoreForTests();

    expect(getStudentId()).toBeNull();
  });

  it("forgets the cached id on reset, so one test cannot leak its id into the next", () => {
    setStudentId(VALID_ID);
    window.localStorage.clear();
    // Clearing storage alone is not enough: the id is cached in the module.
    expect(getStudentId()).toBe(VALID_ID);

    resetStudentIdStoreForTests();

    expect(getStudentId()).toBeNull();
  });
});
