export type TransactionType = "deposit" | "withdraw";

export type TransactionRequest = {
  accountId: number;
  requestId: string;
  type: TransactionType;
  amount: number;
};

export type TransactionRecord = {
  id: number;
  requestId: string;
  accountId: number;
  type: TransactionType;
  amount: number;
  balanceAfter: number;
  createdAt: string;
};

export type AccountResponse = {
  account: {
    id: number;
    accountNumber: string;
    ownerName: string;
    balance: number;
  };
  transactions: TransactionRecord[];
};

export type TransactionResponse = {
  message: string;
  isDuplicate: boolean;
  transaction: TransactionRecord;
};

export const apiUrl = process.env.NEXT_PUBLIC_API_URL || "http://127.0.0.1:5000";

export function formatMoney(amount: number): string {
  return amount.toLocaleString("th-TH", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}
