import { useMemo } from "react";
import DatePicker, { DateObject } from "react-multi-date-picker";
import persian from "react-date-object/calendars/persian";
import persian_fa from "react-date-object/locales/persian_fa";
import gregorian from "react-date-object/calendars/gregorian";
import "react-multi-date-picker/styles/layouts/mobile.css";

interface JalaliDatePickerProps {
  value?: string | null;
  onChange?: (isoDate: string | undefined) => void;
  placeholder?: string;
}

export default function JalaliDatePicker({
  value,
  onChange,
  placeholder = "انتخاب تاریخ",
}: JalaliDatePickerProps) {
  const displayValue = useMemo(() => {
    if (!value) return undefined;
    return new DateObject({ date: value, format: "YYYY-MM-DD", calendar: gregorian }).convert(
      persian
    );
  }, [value]);

  return (
    <DatePicker
      calendar={persian}
      locale={persian_fa}
      value={displayValue}
      onChange={(date: DateObject | null) => {
        if (!date) {
          onChange?.(undefined);
          return;
        }
        const g = date.convert(gregorian);
        const iso = `${g.year.toString().padStart(4, "0")}-${g.month.number
          .toString()
          .padStart(2, "0")}-${g.day.toString().padStart(2, "0")}`;
        onChange?.(iso);
      }}
      placeholder={placeholder}
      inputClass="ant-jalali-input"
      containerStyle={{ width: "100%" }}
      style={{
        width: "100%",
        height: 32,
        padding: "4px 11px",
        border: "1px solid #d9d9d9",
        borderRadius: 6,
        fontSize: 14,
      }}
    />
  );
}
