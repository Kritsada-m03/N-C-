import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Banking Practice | ฝาก–ถอนเงิน",
  description: "โปรเจกต์ฝึก C# และ Next.js สำหรับฝาก ถอน และทดสอบรายการซ้ำ",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="th">
      <body>{children}</body>
    </html>
  );
}
