# Banking Practice — C# + Next.js

โปรเจกต์พื้นฐานสำหรับฝึกทำข้อสอบ: หน้าเดียวสำหรับฝาก–ถอนเงิน แสดงยอดและประวัติ ใช้ C# ASP.NET Core 10, Next.js 16 + TypeScript และ SQLite ที่บันทึกลงไฟล์จริง

ใช้คลาสปกติ, constructor, `if / else`, `foreach`, `try / catch` และ parameterized SQL เพื่อให้อ่าน logic ได้ตรง ๆ

## เปิดโปรเจกต์

ต้องมี .NET SDK 10 และ Node.js 24 LTS

เปิด Terminal แรกที่โฟลเดอร์โปรเจกต์:

```powershell
dotnet restore BankingPractice.sln
dotnet run --project backend/Banking.Api
```

API: http://127.0.0.1:5000 — ตรวจการทำงานได้ที่ http://127.0.0.1:5000/api/health

เปิด Terminal ที่สอง:

```powershell
cd frontend
npm.cmd ci
npm.cmd run dev
```

หน้าเว็บ: http://127.0.0.1:3000

ใช้ `npm.cmd` บน Windows เพื่อไม่ติด PowerShell execution policy หากใช้ macOS/Linux เปลี่ยนเป็น `npm` ได้

ไม่ต้องติดตั้ง database server หรือสร้างฐานข้อมูลเอง API อ่าน `Data/schema.sql` แล้วสร้าง `backend/Banking.Api/Data/banking.db` เมื่อเริ่มทำงาน บัญชี ID 1 เริ่มต้น 10,000 บาท เปิดใหม่จะรักษายอดเดิม

หากเปลี่ยน API URL ให้คัดลอก `frontend/.env.example` เป็น `frontend/.env.local` แล้วแก้ `NEXT_PUBLIC_API_URL` จากนั้นเปิด Next.js ใหม่ ค่า database อยู่ใน `backend/Banking.Api/appsettings.json` หรือ environment variable `Database__Path`

## โครงสร้างและลำดับอ่าน

```text
BankingPractice.sln
backend/
  Banking.Api/
    Controllers/     รับ HTTP แล้วเรียก Service
    Contracts/       คลาส request / response
    Models/          Account และ TransactionRecord
    Services/        ตรวจข้อมูล ป้องกันซ้ำ ฝาก–ถอน commit / rollback
    Data/            เปิด connection, schema และ database
    Banking.Api.http ตัวอย่างเรียก API ผ่าน REST Client
  Banking.Api.Tests/ integration tests กับ SQLite จริงแยกไฟล์ต่อ test
frontend/
  app/page.tsx       หน้าฝาก–ถอน และประวัติ
  app/globals.css    รูปแบบหน้าจอ
  lib/api.ts        รูปแบบข้อมูลและตัวช่วยแสดงจำนวนเงิน
```

เริ่มอ่าน `TransactionRequest.cs` → `TransactionsController.cs` → `BankingService.cs` → `schema.sql` → `page.tsx`

## Logic ฝาก–ถอน

1. ตรวจ account, Request ID, ประเภทรายการ และจำนวนเงิน
2. เปิด connection และ `BeginTransaction(deferred: false)` หรือ `BEGIN IMMEDIATE`
3. ตรวจ Request ID **ภายใน transaction** หากพบข้อมูลเดิม ให้คืนผลเดิม
4. อ่านยอดเงิน ถ้า `deposit` ให้บวก ถ้า `withdraw` ให้ตรวจเงินพอแล้วลบ
5. `UPDATE Accounts` และ `INSERT Transactions` โดยใช้ connection และ transaction เดียวกัน
6. `Commit()` เมื่อสำเร็จ หากมี exception ให้ `Rollback()` แล้วส่ง error

เก็บเงินใน SQLite เป็น `INTEGER` หน่วยสตางค์ เช่น 100.50 บาท = 10050 สตางค์ ส่วน C# ใช้ `decimal` สำหรับคำนวณเงิน รองรับจำนวนเงินและยอดคงเหลือสูงสุด 1,000,000,000 บาท และทศนิยมไม่เกิน 2 ตำแหน่ง

SQLite มี writer ได้ครั้งละหนึ่งราย `BEGIN IMMEDIATE` ขอสิทธิ์เขียนก่อนตรวจซ้ำและอ่านยอด ทำให้คำขอถอนพร้อมกันรออ่านยอดล่าสุด ไม่อ่านยอดเก่าแล้วหักเงินทับกัน Connection รอ lock สูงสุด 10 วินาที หากยังติด lock ส่ง HTTP 503 และให้ลองด้วย ID เดิม การย้ายไป SQL Server ต้องปรับวิธีล็อกให้เหมาะสมด้วย

`RequestId` มี `UNIQUE` constraint ที่ database, บัญชีห้ามยอดติดลบด้วย `CHECK`, ประวัติมี foreign key และทุก SQL ที่รับข้อมูลใช้ parameters

## ป้องกันรายการซ้ำ (idempotency)

- หนึ่งรายการใหม่ใช้ GUID ใหม่ เช่น `crypto.randomUUID()` ที่หน้าบ้าน
- ส่งซ้ำด้วย ID เดิม + บัญชี/ประเภท/จำนวนเงินเดิม: HTTP 200, `isDuplicate: true` และคืนรายการเดิม ไม่เพิ่มประวัติหรือปรับเงินอีก
- ID เดิมแต่รายละเอียดเปลี่ยน: HTTP 409 `REQUEST_ID_CONFLICT`
- หลัง network error / timeout / HTTP 5xx ต้องลองด้วย **ID เดิม** เพราะคำขอแรกอาจ commit ไปแล้ว
- หน้าจอเก็บคำขอที่ยังไม่ยืนยันใน `sessionStorage` ล็อกการแก้ข้อมูล และให้กดส่งรายการเดิมอีกครั้งได้ แม้รีเฟรชหน้าในแท็บเดิม
- หน้าบ้านปิดปุ่มระหว่างส่ง ส่วนการรับประกันว่ารายการไม่ซ้ำจริงอยู่ที่ transaction และ unique constraint ใน backend
- รายการที่ถูกปฏิเสธหรือ rollback ไม่มีประวัติสำเร็จและไม่จอง ID ไว้ถาวร จึงลองด้วย ID เดิมได้

ถ้ารายการเดิมเกิดก่อนรายการอื่น `balanceAfter` ที่คืนเมื่อส่งซ้ำคือยอดหลัง **รายการเดิม** หน้าจอจึงโหลดบัญชีล่าสุดอีกครั้งหลังได้รับผล

## API

| Method | URL | ผลลัพธ์ |
| --- | --- | --- |
| GET | `/api/health` | ตรวจว่า API ทำงาน |
| GET | `/api/accounts/1` | บัญชีและประวัติสำเร็จ 50 รายการล่าสุดจาก snapshot เดียวกัน |
| POST | `/api/transactions` | ฝากหรือถอนเงิน |

```json
{
  "accountId": 1,
  "requestId": "4e4b51d2-367c-4e4c-a333-351684e1a001",
  "type": "deposit",
  "amount": 500.25
}
```

เปลี่ยน `type` เป็น `withdraw` สำหรับถอนเงิน **รายการใหม่ต้องเปลี่ยน Request ID ด้วย**

รายการใหม่ HTTP 201, รายการซ้ำ HTTP 200, ข้อมูลไม่ถูกต้อง/เงินไม่พอ HTTP 400, ไม่พบบัญชี HTTP 404, ID ชนกัน HTTP 409, database busy HTTP 503 และข้อผิดพลาดที่ไม่คาดคิด HTTP 500 ข้อผิดพลาดจาก logic ส่ง `{ "code": "...", "message": "..." }` ส่วน JSON ผิดรูปแบบใช้ Problem Details ของ ASP.NET Core

## ทดสอบ

```powershell
dotnet test BankingPractice.sln
cd frontend
npm.cmd run typecheck
npm.cmd run build
```

เทสต์ครอบคลุมยอดเริ่มต้น, ฝาก–ถอนเศษสตางค์, validation, เงินไม่พอ, ถอนหมดบัญชี, ส่งซ้ำ, ใช้ ID เดิมแต่เปลี่ยนข้อมูล, ส่งซ้ำพร้อมกัน, ถอนพร้อมกัน, unique constraint, เปิด schema ซ้ำ และ **บังคับ INSERT ประวัติให้ล้มเหลวหลัง UPDATE เพื่อพิสูจน์ rollback** โดยใช้ SQLite trigger ในฐานข้อมูลทดสอบ

ลองผ่านหน้าจอ: ฝาก 100 บาท → กด “ทดสอบส่งซ้ำ” → ยอดและจำนวนประวัติต้องเท่าเดิม หรือใช้ตัวอย่างใน `Banking.Api.http`

## ขอบเขตตัวอย่าง

เป็นระบบฝึกในเครื่อง มีบัญชีตัวอย่างเดียว ยังไม่มี login หรือการโอนระหว่างบัญชี ประวัติแสดงเฉพาะรายการสำเร็จ

เอกสารประกอบ: [Next.js installation](https://nextjs.org/docs/app/getting-started/installation), [Microsoft.Data.Sqlite transactions](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions)
