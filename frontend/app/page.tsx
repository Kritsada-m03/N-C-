"use client";

import { FormEvent, useEffect, useRef, useState } from "react";
import {
  AccountResponse,
  apiUrl,
  formatMoney,
  TransactionRequest,
  TransactionResponse,
  TransactionType,
} from "@/lib/api";

const storageKey = "banking-practice.pending-request";

type Notice = { type: "success" | "error" | "info"; text: string };

export default function Home() {
  const [data, setData] = useState<AccountResponse | null>(null);
  const [type, setType] = useState<TransactionType>("deposit");
  const [amount, setAmount] = useState("");
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState("");
  const [notice, setNotice] = useState<Notice | null>(null);
  const [pending, setPending] = useState<TransactionRequest | null>(null);
  const [lastRequest, setLastRequest] = useState<TransactionRequest | null>(null);
  const sending = useRef(false);

  // หาก response หาย ต้องลองด้วย ID เดิม ไม่สร้างรายการใหม่
  async function loadAccount() {
    setLoading(true);
    try {
      const response = await fetch(`${apiUrl}/api/accounts/1`, {
        cache: "no-store",
        signal: AbortSignal.timeout(15000),
      });
      if (!response.ok) {
        throw new Error("โหลดข้อมูลบัญชีไม่สำเร็จ");
      }
      const account: AccountResponse = await response.json();
      setData(account);
      setLoadError("");
    } catch {
      setLoadError("เชื่อมต่อ API ไม่สำเร็จ ตรวจสอบว่า C# API รันที่พอร์ต 5000 แล้วกดรีเฟรช");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void loadAccount();
    try {
      const saved = sessionStorage.getItem(storageKey);
      if (saved) {
        const request: TransactionRequest = JSON.parse(saved);
        if (
          request.accountId === 1 &&
          typeof request.requestId === "string" &&
          /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(request.requestId) &&
          (request.type === "deposit" || request.type === "withdraw") &&
          typeof request.amount === "number" && request.amount > 0
        ) {
          setPending(request);
          setType(request.type);
          setAmount(String(request.amount));
          setNotice({ type: "info", text: "พบรายการที่ยังไม่ได้รับผลยืนยัน กรุณาส่งรายการเดิมอีกครั้งเพื่อตรวจสอบผล" });
        } else {
          sessionStorage.removeItem(storageKey);
        }
      }
    } catch {
      // ยัง retry ด้วย state ในหน้านี้ได้ หาก browser ปิด sessionStorage
    }
  }, []);

  function rememberPending(request: TransactionRequest | null) {
    setPending(request);
    try {
      if (request) {
        sessionStorage.setItem(storageKey, JSON.stringify(request));
      } else {
        sessionStorage.removeItem(storageKey);
      }
    } catch {
      // sessionStorage เป็นตัวช่วย การป้องกันรายการซ้ำจริงอยู่ที่ API + database
    }
  }

  async function sendRequest(request: TransactionRequest) {
    // ref กันการกดติดกันก่อน React render รอบถัดไป
    if (sending.current) return;
    sending.current = true;
    setBusy(true);
    setNotice(null);
    rememberPending(request);

    try {
      const response = await fetch(`${apiUrl}/api/transactions`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(request),
        signal: AbortSignal.timeout(15000),
      });
      const result = await response.json();

      if (!response.ok) {
        if (response.status < 500) {
          rememberPending(null);
          setNotice({ type: "error", text: result.message || result.title || "ข้อมูลรายการไม่ถูกต้อง" });
        } else {
          setNotice({ type: "error", text: `${result.message || "ยังยืนยันผลรายการไม่ได้"} กดส่งรายการเดิมอีกครั้งได้โดยไม่หักหรือเพิ่มเงินซ้ำ` });
        }
        await loadAccount();
        return;
      }

      const success: TransactionResponse = result;
      rememberPending(null);
      setLastRequest(request);
      setAmount("");
      if (success.isDuplicate) {
        setNotice({ type: "info", text: "ตรวจพบ Request ID เดิม คืนผลรายการเดิมสำเร็จ โดยไม่ปรับยอดเงินซ้ำ" });
      } else {
        const label = request.type === "deposit" ? "ฝากเงิน" : "ถอนเงิน";
        setNotice({ type: "success", text: `${label} ${formatMoney(request.amount)} บาท สำเร็จ` });
      }
      await loadAccount();
    } catch {
      setNotice({ type: "error", text: "ยังไม่ได้รับผลยืนยันจาก API กรุณากดส่งรายการเดิมอีกครั้ง ระบบจะใช้ Request ID เดิมเพื่อตรวจสอบและป้องกันรายการซ้ำ" });
    } finally {
      sending.current = false;
      setBusy(false);
    }
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (pending) {
      void sendRequest(pending);
      return;
    }

    if (!/^\d+(\.\d{1,2})?$/.test(amount.trim())) {
      setNotice({ type: "error", text: "กรุณาระบุจำนวนเงินเป็นตัวเลข ทศนิยมไม่เกิน 2 ตำแหน่ง" });
      return;
    }

    const value = Number(amount);
    if (value <= 0 || value > 1000000000) {
      setNotice({ type: "error", text: "จำนวนเงินต้องมากกว่า 0 และไม่เกิน 1,000,000,000 บาท" });
      return;
    }

    void sendRequest({ accountId: 1, requestId: crypto.randomUUID(), type, amount: value });
  }

  const locked = busy || pending !== null;

  return (
    <>
      <header className="topbar">
        <div className="topbar-inner">
          <a className="brand" href="/" aria-label="Banking Practice หน้าแรก">
            <span className="brand-icon" aria-hidden="true">b.</span>
            <span>banking<span className="brand-light">practice</span></span>
          </a>
          <span className="demo-badge"><span /> โปรเจกต์ฝึกทดสอบ</span>
        </div>
      </header>

      <main>
        <div className="page-heading">
          <div>
            <p className="eyebrow">YOUR EVERYDAY BANKING</p>
            <h1>จัดการเงินในบัญชี</h1>
            <p className="subtitle">ฝาก ถอน และตรวจสอบรายการในหน้าเดียว</p>
          </div>
          <span className="page-number">01 / บัญชีของฉัน</span>
        </div>

        {loadError && <div className="notice error" role="alert">{loadError}</div>}

        <div className="workspace">
          <section className="account-section" aria-label="ข้อมูลบัญชี">
            <div className="account-card">
              <div className="account-card-top">
                <span>บัญชีออมทรัพย์</span><span className="card-symbol" aria-hidden="true">↗</span>
              </div>
              <p className="balance-label">ยอดเงินคงเหลือ</p>
              <p className="balance">{data ? formatMoney(data.account.balance) : "—"}<span>THB</span></p>
              <div className="account-card-bottom">
                <div><p>{data?.account.ownerName || "บัญชีฝึกทดสอบ"}</p><span>{data?.account.accountNumber || "100-0-00001-1"}</span></div>
                <span className="card-mark" aria-hidden="true">b.</span>
              </div>
            </div>
            <div className="account-note"><span aria-hidden="true">ⓘ</span><p>บัญชีตัวอย่างเริ่มต้น 10,000.00 บาท<br />ข้อมูลถูกบันทึกลงฐานข้อมูล SQLite จริง</p></div>
            <div className="flow-note">
              <p className="eyebrow">BEHIND THE TRANSACTION</p>
              <h3>หนึ่งรายการ ครบทุกขั้นตอน</h3>
              <ol><li>ตรวจสอบข้อมูลและรายการซ้ำ</li><li>ปรับยอดเงินและบันทึกประวัติ</li><li>Commit พร้อมกัน หรือ Rollback ทั้งหมด</li></ol>
            </div>
          </section>

          <section className="panel transaction-panel" aria-labelledby="transaction-title">
            <div className="panel-heading"><h2 id="transaction-title">ทำรายการ</h2><span className="small-label">DEPOSIT / WITHDRAW</span></div>
            <div className="tabs" role="group" aria-label="ประเภทรายการ">
              <button type="button" className={type === "deposit" ? "tab active" : "tab"} aria-pressed={type === "deposit"} disabled={locked} onClick={() => setType("deposit")}><span aria-hidden="true">↙</span> ฝากเงิน</button>
              <button type="button" className={type === "withdraw" ? "tab active" : "tab"} aria-pressed={type === "withdraw"} disabled={locked} onClick={() => setType("withdraw")}><span aria-hidden="true">↗</span> ถอนเงิน</button>
            </div>
            <form onSubmit={handleSubmit} noValidate>
              <label className="field-label" htmlFor="amount">จำนวนเงิน</label>
              <div className="amount-field"><input id="amount" inputMode="decimal" autoComplete="off" placeholder="0.00" value={amount} disabled={locked} onChange={(event) => setAmount(event.target.value)} aria-describedby="amount-help" /><span>บาท</span></div>
              <p className="field-help" id="amount-help">ระบุจำนวนเงินมากกว่า 0 บาท · ทศนิยมไม่เกิน 2 ตำแหน่ง</p>
              <div className="quick-amounts" aria-label="จำนวนเงินที่เลือกได้เร็ว">
                {[100, 500, 1000].map((value) => <button key={value} type="button" disabled={locked} onClick={() => setAmount(String(value))}>{value.toLocaleString("th-TH")} ฿</button>)}
              </div>

              {notice && <div className={`notice ${notice.type}`} role={notice.type === "error" ? "alert" : "status"}>{notice.text}</div>}
              {pending && <p className="request-reference">Request ID: <code>{pending.requestId}</code></p>}

              <button className={`submit-button ${type}`} type="submit" disabled={busy || (!data && !pending)}>
                {busy ? "กำลังตรวจสอบรายการ…" : pending ? "ส่งรายการเดิมอีกครั้ง" : type === "deposit" ? "ยืนยันฝากเงิน" : "ยืนยันถอนเงิน"}<span aria-hidden="true">→</span>
              </button>
            </form>
            <div className="duplicate-section"><div><strong>ทดลองป้องกันรายการซ้ำ</strong><p>ส่งรายการสำเร็จล่าสุดด้วย Request ID เดิม</p></div><button type="button" className="text-button" disabled={!lastRequest || locked} onClick={() => { if (lastRequest) void sendRequest(lastRequest); }}>ทดสอบส่งซ้ำ ↻</button></div>
          </section>
        </div>

        <section className="panel history-panel" aria-labelledby="history-title">
          <div className="panel-heading"><div className="history-title"><h2 id="history-title">ประวัติรายการ</h2><span className="count">{data?.transactions.length || 0}</span></div><button className="text-button" type="button" disabled={loading || busy} onClick={() => void loadAccount()}>{loading ? "กำลังโหลด…" : "รีเฟรช ↻"}</button></div>
          <p className="history-description">แสดงรายการสำเร็จล่าสุดสูงสุด 50 รายการ · เวลาไทย</p>
          <div className="table-scroll">
            <table>
              <thead><tr><th>รายการ / วันเวลา</th><th>Request ID</th><th className="number">จำนวนเงิน (บาท)</th><th className="number">ยอดคงเหลือ (บาท)</th></tr></thead>
              <tbody>
                {data?.transactions.map((record) => (
                  <tr key={record.id}>
                    <td><div className="transaction-cell"><span className={`transaction-icon ${record.type}`} aria-hidden="true">{record.type === "deposit" ? "↙" : "↗"}</span><div><strong>{record.type === "deposit" ? "ฝากเงิน" : "ถอนเงิน"}</strong><time dateTime={record.createdAt}>{new Date(record.createdAt).toLocaleString("th-TH", { timeZone: "Asia/Bangkok", dateStyle: "short", timeStyle: "medium" })}</time></div></div></td>
                    <td><code className="history-request" title={record.requestId}>{record.requestId}</code></td>
                    <td className={`number money ${record.type}`}>{record.type === "deposit" ? "+" : "−"}{formatMoney(record.amount)}</td>
                    <td className="number money">{formatMoney(record.balanceAfter)}</td>
                  </tr>
                ))}
                {!data?.transactions.length && <tr><td colSpan={4} className="empty-state"><span aria-hidden="true">↔</span><strong>{loading ? "กำลังโหลดข้อมูล" : loadError ? "ยังโหลดประวัติไม่ได้" : "ยังไม่มีรายการ"}</strong><p>{loadError ? "เปิด C# API แล้วกดรีเฟรชอีกครั้ง" : "เริ่มฝากหรือถอนเงิน รายการของคุณจะแสดงที่นี่"}</p></td></tr>}
              </tbody>
            </table>
          </div>
        </section>

        <footer><span>Banking Practice</span><p>C# + Next.js + SQLite · สำหรับฝึกเขียนโปรแกรม</p></footer>
      </main>
    </>
  );
}
