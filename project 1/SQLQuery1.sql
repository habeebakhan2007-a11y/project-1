-- 1. Create Database if not exists
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'PiaPortalDb')
BEGIN
    CREATE DATABASE PiaPortalDb;
END
GO

USE PiaPortalDb;
GO

-- 2. Clean Existing Tables (Drop Sequence)
IF OBJECT_ID('dbo.LeaveApplications', 'U') IS NOT NULL DROP TABLE dbo.LeaveApplications;
IF OBJECT_ID('dbo.UserLeaveBalances', 'U') IS NOT NULL DROP TABLE dbo.UserLeaveBalances;
IF OBJECT_ID('dbo.Users', 'U') IS NOT NULL DROP TABLE dbo.Users;
GO

-- 3. Create Users Table
CREATE TABLE Users (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    PNo VARCHAR(20) NOT NULL UNIQUE,
    FullName VARCHAR(100) NOT NULL,
    Email VARCHAR(100) NOT NULL UNIQUE,
    Password VARCHAR(100) NOT NULL,
    Role VARCHAR(50) NOT NULL
);
GO

-- 4. Insert Default Users
INSERT INTO Users (PNo, FullName, Email, Password, Role) VALUES
('1001', 'Admin', 'admin.portal@gmail.com', 'Admin123', 'Admin'),
('2002', 'General Manager', 'gm.operations@gmail.com', 'GM123', 'General Manager'),
('3003', 'Deputy GM', 'dgm.flightops@gmail.com', 'DGM123', 'Deputy GM'),
('4004', 'habeeba', 'habeeba@gmail.com', 'Emp123', 'Employee'),
('4005', 'Sarah Khan', 'sarah.khan@gmail.com', 'Emp123', 'Employee'),
('4006', 'Ali Raza', 'ali.raza@gmail.com', 'Emp123', 'Employee');
GO

-- 5. Create Leave Applications Table
CREATE TABLE LeaveApplications (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    ApplicantPNo VARCHAR(20) NOT NULL,
    ApplicantName VARCHAR(100) NOT NULL,
    LeaveType VARCHAR(50) NOT NULL,
    StartDate DATE NOT NULL,
    EndDate DATE NOT NULL,
    TotalDays INT NOT NULL DEFAULT 1,
    Reason VARCHAR(250) NOT NULL,
    AddressOnLeave VARCHAR(250) NULL,
    ContactNo VARCHAR(20) NULL,
    CityOnLeave VARCHAR(100) NULL,
    ManagerPNo VARCHAR(100) NULL,
    DocumentPath VARCHAR(250) NULL,
    Status VARCHAR(50) DEFAULT 'Pending Executive Review',
    AssignedToRole VARCHAR(50) NULL,
    ExecutiveRemarks VARCHAR(MAX) NULL,
    AdminRemarks VARCHAR(MAX) NULL,
    ApproverRemarks VARCHAR(250) NULL,
    AppliedDate DATETIME DEFAULT GETDATE()
);
GO

-- 6. Create User Leave Balances Table (Updated with UpdatedAt for Quota Reset logic)
CREATE TABLE UserLeaveBalances (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    PNo VARCHAR(20) NOT NULL UNIQUE,
    CasualQuota INT NOT NULL DEFAULT 5,
    MedicalQuota INT NOT NULL DEFAULT 5,
    AnnualQuota INT NOT NULL DEFAULT 5,
    UpdatedAt DATETIME DEFAULT GETDATE()
);
GO

-- 7. Insert Initial Leave Balances
INSERT INTO UserLeaveBalances (PNo, CasualQuota, MedicalQuota, AnnualQuota, UpdatedAt) VALUES
('1001', 5, 5, 5, GETDATE()),
('2002', 5, 5, 5, GETDATE()),
('3003', 5, 5, 5, GETDATE()),
('4004', 5, 5, 5, GETDATE()),
('4005', 5, 5, 5, GETDATE()),
('4006', 5, 5, 5, GETDATE());
GO

-- 8. Verify Tables Data
SELECT * FROM Users;
SELECT * FROM LeaveApplications;
SELECT * FROM UserLeaveBalances;
GO