USE master;
GO
IF DB_ID('AGRechnungDev') IS NULL
    CREATE DATABASE AGRechnungDev;
GO
USE AGRechnungDev;
GO

/* ──────────────────────────────
   SCHEMAS
──────────────────────────────── */
CREATE SCHEMA auth;
GO
CREATE SCHEMA core;
GO
CREATE SCHEMA billing;
GO
CREATE SCHEMA sales;
GO

/* ──────────────────────────────
   auth.Users
──────────────────────────────── */
CREATE TABLE auth.Users (
    Id            INT IDENTITY(1,1) PRIMARY KEY,
    Email         NVARCHAR(255) NOT NULL UNIQUE,
    VerifiedEmail BIT NOT NULL DEFAULT 0,
    Active        BIT NOT NULL DEFAULT 1,
    CreatedAt     DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt     DATETIME2(0) NOT NULL DEFAULT SYSDATETIME()
);
GO
CREATE INDEX IX_Users_Email ON auth.Users(Email);
GO

/* Trigger → auto-update UpdatedAt */
CREATE OR ALTER TRIGGER trg_Users_Update
ON auth.Users
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE u
        SET UpdatedAt = SYSDATETIME()
    FROM auth.Users u
    INNER JOIN inserted i ON u.Id = i.Id;
END;
GO

/* ──────────────────────────────
   auth.VerificationTokens
──────────────────────────────── */
CREATE TABLE auth.VerificationTokens (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    UserId     INT NOT NULL UNIQUE,
    Token      NVARCHAR(255) NOT NULL,
    ExpiresAt  DATETIME2(0) NOT NULL,
    CreatedAt  DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt  DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_VerificationTokens_Users
        FOREIGN KEY (UserId) REFERENCES auth.Users(Id)
        ON DELETE CASCADE ON UPDATE CASCADE
);
GO
-- Ensure ExpiresAt column exists (idempotent for reruns)
IF COL_LENGTH('auth.VerificationTokens', 'ExpiresAt') IS NULL
BEGIN
    ALTER TABLE auth.VerificationTokens
    ADD ExpiresAt DATETIME2(0) NOT NULL CONSTRAINT DF_VerificationTokens_ExpiresAt DEFAULT SYSUTCDATETIME();
END;
GO
CREATE INDEX IX_VerificationTokens_UserId ON auth.VerificationTokens(UserId);
GO

CREATE OR ALTER TRIGGER trg_VerificationTokens_Update
ON auth.VerificationTokens
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE vt
        SET UpdatedAt = SYSDATETIME()
    FROM auth.VerificationTokens vt
    INNER JOIN inserted i ON vt.Id = i.Id;
END;
GO

/* ──────────────────────────────
   core.Companies
──────────────────────────────── */
CREATE TABLE core.Companies (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    UserId      INT NOT NULL,
    Name        NVARCHAR(255) NOT NULL,
    VatNumber   NVARCHAR(50) NULL,
    CreatedAt   DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt   DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_Companies_Users
        FOREIGN KEY (UserId) REFERENCES auth.Users(Id)
        ON DELETE CASCADE ON UPDATE CASCADE
);
GO
CREATE INDEX IX_Companies_UserId ON core.Companies(UserId);
GO

CREATE OR ALTER TRIGGER trg_Companies_Update
ON core.Companies
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE c
        SET UpdatedAt = SYSDATETIME()
    FROM core.Companies c
    INNER JOIN inserted i ON c.Id = i.Id;
END;
GO

/* ──────────────────────────────
   core.Addresses
──────────────────────────────── */
CREATE TABLE core.Addresses (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    UserId      INT NULL,
    CompanyId   INT NULL,
    Street      NVARCHAR(255) NOT NULL,
    PostalCode  NVARCHAR(20) NOT NULL,
    City        NVARCHAR(100) NOT NULL,
    Country     NVARCHAR(100) NOT NULL DEFAULT N'Deutschland',
    CreatedAt   DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt   DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_Addresses_Users
        FOREIGN KEY (UserId) REFERENCES auth.Users(Id)
        ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT FK_Addresses_Companies
        FOREIGN KEY (CompanyId) REFERENCES core.Companies(Id)
        ON DELETE CASCADE ON UPDATE CASCADE
);
GO
CREATE INDEX IX_Addresses_UserId ON core.Addresses(UserId);
CREATE INDEX IX_Addresses_CompanyId ON core.Addresses(CompanyId);
GO

CREATE OR ALTER TRIGGER trg_Addresses_Update
ON core.Addresses
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE a
        SET UpdatedAt = SYSDATETIME()
    FROM core.Addresses a
    INNER JOIN inserted i ON a.Id = i.Id;
END;
GO

/* ──────────────────────────────
   billing.TaxRateTemplates
──────────────────────────────── */
CREATE TABLE billing.TaxRateTemplates (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    Name       NVARCHAR(100) NOT NULL,
    Rate       DECIMAL(10,2) NOT NULL,
    CreatedAt  DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt  DATETIME2(0) NOT NULL DEFAULT SYSDATETIME()
);
GO

CREATE OR ALTER TRIGGER trg_TaxRateTemplates_Update
ON billing.TaxRateTemplates
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE t
        SET UpdatedAt = SYSDATETIME()
    FROM billing.TaxRateTemplates t
    INNER JOIN inserted i ON t.Id = i.Id;
END;
GO

/* ──────────────────────────────
   billing.TaxRates
──────────────────────────────── */
CREATE TABLE billing.TaxRates (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    CompanyId   INT NOT NULL,
    TemplateId  INT NOT NULL,
    Rate        DECIMAL(10,2) NOT NULL,
    CreatedAt   DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt   DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_TaxRates_Companies
        FOREIGN KEY (CompanyId) REFERENCES core.Companies(Id)
        ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT FK_TaxRates_Templates
        FOREIGN KEY (TemplateId) REFERENCES billing.TaxRateTemplates(Id)
        ON DELETE CASCADE ON UPDATE CASCADE
);
GO
CREATE INDEX IX_TaxRates_CompanyId ON billing.TaxRates(CompanyId);
GO

CREATE OR ALTER TRIGGER trg_TaxRates_Update
ON billing.TaxRates
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE tr
        SET UpdatedAt = SYSDATETIME()
    FROM billing.TaxRates tr
    INNER JOIN inserted i ON tr.Id = i.Id;
END;
GO

/* ──────────────────────────────
   billing.Bills
──────────────────────────────── */
CREATE TABLE billing.Bills (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    CompanyId   INT NOT NULL,
    BillNumber  NVARCHAR(50) NOT NULL,
    TotalAmount DECIMAL(10,2) NOT NULL,
    DueDate     DATE NOT NULL,
    Paid        BIT NOT NULL DEFAULT 0,
    CreatedAt   DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt   DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_Bills_Companies
        FOREIGN KEY (CompanyId) REFERENCES core.Companies(Id)
        ON DELETE CASCADE ON UPDATE CASCADE
);
GO
CREATE INDEX IX_Bills_CompanyId ON billing.Bills(CompanyId);
GO

CREATE OR ALTER TRIGGER trg_Bills_Update
ON billing.Bills
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE b
        SET UpdatedAt = SYSDATETIME()
    FROM billing.Bills b
    INNER JOIN inserted i ON b.Id = i.Id;
END;
GO

/* ──────────────────────────────
   billing.PaymentReminders
──────────────────────────────── */
CREATE TABLE billing.PaymentReminders (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    BillId     INT NOT NULL,
    ReminderDate DATE NOT NULL,
    CreatedAt  DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt  DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_PaymentReminders_Bills
        FOREIGN KEY (BillId) REFERENCES billing.Bills(Id)
        ON DELETE CASCADE ON UPDATE CASCADE
);
GO
CREATE INDEX IX_PaymentReminders_BillId ON billing.PaymentReminders(BillId);
GO

CREATE OR ALTER TRIGGER trg_PaymentReminders_Update
ON billing.PaymentReminders
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE pr
        SET UpdatedAt = SYSDATETIME()
    FROM billing.PaymentReminders pr
    INNER JOIN inserted i ON pr.Id = i.Id;
END;
GO

/* ──────────────────────────────
   sales.Services
──────────────────────────────── */
CREATE TABLE sales.Services (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    Name       NVARCHAR(255) NOT NULL,
    Description NVARCHAR(500) NULL,
    Price      DECIMAL(10,2) NOT NULL,
    CreatedAt  DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt  DATETIME2(0) NOT NULL DEFAULT SYSDATETIME()
);
GO

CREATE OR ALTER TRIGGER trg_Services_Update
ON sales.Services
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE s
        SET UpdatedAt = SYSDATETIME()
    FROM sales.Services s
    INNER JOIN inserted i ON s.Id = i.Id;
END;
GO

/* ──────────────────────────────
   sales.ServiceTemplates
──────────────────────────────── */
CREATE TABLE sales.ServiceTemplates (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    CompanyId  INT NOT NULL,
    Name       NVARCHAR(255) NOT NULL,
    Price      DECIMAL(10,2) NOT NULL,
    CreatedAt  DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt  DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_ServiceTemplates_Companies
        FOREIGN KEY (CompanyId) REFERENCES core.Companies(Id)
        ON DELETE CASCADE ON UPDATE CASCADE
);
GO

CREATE OR ALTER TRIGGER trg_ServiceTemplates_Update
ON sales.ServiceTemplates
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE st
        SET UpdatedAt = SYSDATETIME()
    FROM sales.ServiceTemplates st
    INNER JOIN inserted i ON st.Id = i.Id;
END;
GO

/* ──────────────────────────────
   sales.Offers
──────────────────────────────── */
CREATE TABLE sales.Offers (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    CompanyId   INT NOT NULL,
    OfferNumber NVARCHAR(50) NOT NULL,
    TotalAmount DECIMAL(10,2) NOT NULL,
    ValidUntil  DATE NOT NULL,
    CreatedAt   DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt   DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_Offers_Companies
        FOREIGN KEY (CompanyId) REFERENCES core.Companies(Id)
        ON DELETE CASCADE ON UPDATE CASCADE
);
GO
CREATE INDEX IX_Offers_CompanyId ON sales.Offers(CompanyId);
GO

CREATE OR ALTER TRIGGER trg_Offers_Update
ON sales.Offers
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE o
        SET UpdatedAt = SYSDATETIME()
    FROM sales.Offers o
    INNER JOIN inserted i ON o.Id = i.Id;
END;
GO

/* ──────────────────────────────
   sales.OfferServices
──────────────────────────────── */
CREATE TABLE sales.OfferServices (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    OfferId    INT NOT NULL,
    ServiceId  INT NOT NULL,
    Quantity   INT NOT NULL DEFAULT 1,
    CreatedAt  DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    UpdatedAt  DATETIME2(0) NOT NULL DEFAULT SYSDATETIME(),
    CONSTRAINT FK_OfferServices_Offers
        FOREIGN KEY (OfferId) REFERENCES sales.Offers(Id)
        ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT FK_OfferServices_Services
        FOREIGN KEY (ServiceId) REFERENCES sales.Services(Id)
        ON DELETE CASCADE ON UPDATE CASCADE
);
GO
CREATE INDEX IX_OfferServices_OfferId ON sales.OfferServices(OfferId);
CREATE INDEX IX_OfferServices_ServiceId ON sales.OfferServices(ServiceId);
GO

CREATE OR ALTER TRIGGER trg_OfferServices_Update
ON sales.OfferServices
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE os
        SET UpdatedAt = SYSDATETIME()
    FROM sales.OfferServices os
    INNER JOIN inserted i ON os.Id = i.Id;
END;
GO