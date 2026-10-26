-- เก็บเงินเป็น INTEGER หน่วยสตางค์ เช่น 100.50 บาท = 10050 สตางค์
CREATE TABLE IF NOT EXISTS Accounts (
    Id INTEGER PRIMARY KEY,
    AccountNumber TEXT NOT NULL UNIQUE,
    OwnerName TEXT NOT NULL,
    BalanceSatang INTEGER NOT NULL
        CHECK (typeof(BalanceSatang) = 'integer' AND BalanceSatang BETWEEN 0 AND 100000000000)
);

CREATE TABLE IF NOT EXISTS Transactions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    RequestId TEXT NOT NULL UNIQUE,
    AccountId INTEGER NOT NULL REFERENCES Accounts(Id),
    Type TEXT NOT NULL CHECK (Type IN ('deposit', 'withdraw')),
    AmountSatang INTEGER NOT NULL
        CHECK (typeof(AmountSatang) = 'integer' AND AmountSatang BETWEEN 1 AND 100000000000),
    BalanceAfterSatang INTEGER NOT NULL
        CHECK (typeof(BalanceAfterSatang) = 'integer' AND BalanceAfterSatang BETWEEN 0 AND 100000000000),
    CreatedAt TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_Transactions_AccountId_Id
    ON Transactions(AccountId, Id DESC);

-- INSERT OR IGNORE ทำให้เปิดโปรแกรมใหม่แล้วไม่ reset ยอดเงินเดิม
INSERT OR IGNORE INTO Accounts (Id, AccountNumber, OwnerName, BalanceSatang)
VALUES (1, '100-0-00001-1', 'บัญชีฝึกทดสอบ', 1000000);
