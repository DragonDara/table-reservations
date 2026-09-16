/** Kazakhstan mobile: +7 + DEF code (70x / 747 / 77x) + 7 digits. */
const KZ_MOBILE = /^\+7(70[0-8]|747|77[1578])\d{7}$/;

export const KZ_PHONE_INVALID_MESSAGE =
  "Укажите казахстанский мобильный номер: +7 700 000 00 00.";

export function formatKazakhstanPhone(rawValue: string): string {
  let digits = rawValue.replace(/\D/g, "");

  // человек часто сам вбивает код страны (7 или 8) — убираем, он и так будет "+7"
  if (digits.startsWith("7") || digits.startsWith("8")) {
    digits = digits.slice(1);
  }

  digits = digits.slice(0, 10);

  const code = digits.slice(0, 3);
  const part1 = digits.slice(3, 6);
  const part2 = digits.slice(6, 8);
  const part3 = digits.slice(8, 10);

  let result = "+7";
  if (code) result += ` ${code}`;
  if (part1) result += ` (${part1})`;
  if (part2) result += ` ${part2}`;
  if (part3) result += ` ${part3}`;

  return result;
}

export function toE164Digits(value: string): string {
  return `+${value.replace(/\D/g, "")}`;
}

export function isKazakhstanMobile(phone: string): boolean {
  return KZ_MOBILE.test(phone);
}
