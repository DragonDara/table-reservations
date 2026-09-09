// Public startup fallbacks from appsettings.json -> Frontend.Links.WhatsApp.
// Keep these in sync when changing an organization's public WhatsApp contact.
// Loaded tenant settings replace these as soon as the API responds.
const contacts = new Map([
  ['thetochka', 'https://wa.me/77751516189?text=Здравствуйте! Пишу из сайта Bron'],
  ['thetochka-carwasher', 'https://api.whatsapp.com/send/?phone=77475569530&text&type=phone_number&app_absent=0&utm_source=ig'],
]);

export function initialErrorWhatsApp(hostname: string, fallbackOrganization: string | null): string | null {
  const host = hostname.toLowerCase().replace(/\.$/, '');
  for (const suffix of ['.bron.cafe', '.localhost']) {
    if (host.endsWith(suffix)) {
      const tenant = host.slice(0, -suffix.length);
      if (tenant !== 'www') return contacts.get(tenant) ?? null;
    }
  }
  return contacts.get(fallbackOrganization?.toLowerCase() ?? '') ?? null;
}
