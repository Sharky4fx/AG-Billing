USE AGRechnungDev;
GO

IF OBJECT_ID('sales.OfferServices', 'U')       IS NOT NULL DROP TABLE sales.OfferServices;
IF OBJECT_ID('sales.Offers', 'U')              IS NOT NULL DROP TABLE sales.Offers;
IF OBJECT_ID('sales.ServiceTemplates', 'U')    IS NOT NULL DROP TABLE sales.ServiceTemplates;
IF OBJECT_ID('sales.Services', 'U')            IS NOT NULL DROP TABLE sales.Services;

IF OBJECT_ID('billing.PaymentReminders', 'U')  IS NOT NULL DROP TABLE billing.PaymentReminders;
IF OBJECT_ID('billing.Bills', 'U')             IS NOT NULL DROP TABLE billing.Bills;
IF OBJECT_ID('billing.TaxRates', 'U')          IS NOT NULL DROP TABLE billing.TaxRates;
IF OBJECT_ID('billing.TaxRateTemplates', 'U')  IS NOT NULL DROP TABLE billing.TaxRateTemplates;

IF OBJECT_ID('core.Addresses', 'U')           IS NOT NULL DROP TABLE core.Addresses;
IF OBJECT_ID('core.Companies', 'U')           IS NOT NULL DROP TABLE core.Companies;

IF OBJECT_ID('auth.VerificationTokens', 'U')  IS NOT NULL DROP TABLE auth.VerificationTokens;
IF OBJECT_ID('auth.Users', 'U')               IS NOT NULL DROP TABLE auth.Users;
GO


IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'sales')   DROP SCHEMA sales;
IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'billing') DROP SCHEMA billing;
IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'core')    DROP SCHEMA core;
IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'auth')    DROP SCHEMA auth;
GO
