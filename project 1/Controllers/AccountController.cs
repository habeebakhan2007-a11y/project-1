using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using project_1.Models;
using System.Data;

namespace project_1.Controllers
{
    public class AccountController : Controller
    {
        private readonly IConfiguration _configuration;

        public AccountController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string GetConnectionString() =>
            _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        // ==========================================
        // 1. AUTHENTICATION (Login & Logout)
        // ==========================================

        [HttpGet]
        public IActionResult Login()
        {
            if (!string.IsNullOrEmpty(HttpContext.Session.GetString("Role")))
            {
                return RedirectToAction("Dashboard");
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string pno, string password)
        {
            if (string.IsNullOrWhiteSpace(pno) || string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "Please enter both P.No and Password!";
                return View();
            }

            try
            {
                await using var conn = new SqlConnection(GetConnectionString());
                string query = "SELECT PNo, FullName, Role, Password FROM Users WHERE TRIM(PNo) = TRIM(@PNo)";
                await using var cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@PNo", pno.Trim());

                await conn.OpenAsync();
                await using var dr = await cmd.ExecuteReaderAsync();

                if (await dr.ReadAsync())
                {
                    string storedHash = dr["Password"].ToString()!;
                    bool isValidPassword = false;

                    try
                    {
                        isValidPassword = BCrypt.Net.BCrypt.Verify(password, storedHash);
                    }
                    catch (BCrypt.Net.SaltParseException)
                    {
                        if (password == storedHash)
                        {
                            isValidPassword = true;
                        }
                    }

                    if (isValidPassword)
                    {
                        HttpContext.Session.SetString("PNo", dr["PNo"].ToString()!);
                        HttpContext.Session.SetString("FullName", dr["FullName"].ToString()!);
                        HttpContext.Session.SetString("Role", dr["Role"].ToString()!);

                        return RedirectToAction("Dashboard");
                    }
                }

                ViewBag.Error = "Invalid P.No or Password!";
            }
            catch (Exception ex)
            {
                ViewBag.Error = "Authentication Error: " + ex.Message;
            }

            return View();
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        // ==========================================
        // 2. DASHBOARD
        // ==========================================

        public IActionResult Dashboard()
        {
            string? role = HttpContext.Session.GetString("Role");
            if (string.IsNullOrEmpty(role)) return RedirectToAction("Login");

            ViewBag.PNo = HttpContext.Session.GetString("PNo");
            ViewBag.FullName = HttpContext.Session.GetString("FullName");
            ViewBag.Role = role;

            return View();
        }

        // ==========================================
        // 3. LEAVE APPLICATIONS (Apply & View)
        // ==========================================

        [HttpGet]
        public async Task<IActionResult> ApplyLeave()
        {
            string? userPNo = HttpContext.Session.GetString("PNo");
            if (string.IsNullOrEmpty(userPNo)) return RedirectToAction("Login");

            await PopulateLeaveBalancesAndManagersAsync(userPNo);
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApplyLeave(LeaveApplication model)
        {
            string? userPNo = HttpContext.Session.GetString("PNo");
            string? userName = HttpContext.Session.GetString("FullName");

            if (string.IsNullOrEmpty(userPNo)) return RedirectToAction("Login");

            if (string.IsNullOrWhiteSpace(model.ManagerPNo))
            {
                ModelState.AddModelError("", "Please select a General Manager or Deputy GM.");
                await PopulateLeaveBalancesAndManagersAsync(userPNo);
                return View(model);
            }

            try
            {
                await using var conn = new SqlConnection(GetConnectionString());
                string query = @"INSERT INTO LeaveApplications 
                                (ApplicantPNo, ApplicantName, StartDate, EndDate, LeaveType, ManagerPNo, AddressOnLeave, Reason, ContactNo, CityOnLeave, Status, AppliedDate) 
                                VALUES 
                                (@ApplicantPNo, @ApplicantName, @StartDate, @EndDate, @LeaveType, @ManagerPNo, @AddressOnLeave, @Reason, @ContactNo, @CityOnLeave, 'Pending Executive Review', GETDATE())";

                await using var cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@ApplicantPNo", userPNo);
                cmd.Parameters.AddWithValue("@ApplicantName", userName ?? string.Empty);
                cmd.Parameters.AddWithValue("@StartDate", model.StartDate);
                cmd.Parameters.AddWithValue("@EndDate", model.EndDate);
                cmd.Parameters.AddWithValue("@LeaveType", model.LeaveType ?? string.Empty);
                cmd.Parameters.AddWithValue("@ManagerPNo", model.ManagerPNo.Trim());
                cmd.Parameters.AddWithValue("@AddressOnLeave", model.AddressOnLeave ?? string.Empty);
                cmd.Parameters.AddWithValue("@Reason", model.Reason ?? string.Empty);
                cmd.Parameters.AddWithValue("@ContactNo", model.ContactNo ?? string.Empty);
                cmd.Parameters.AddWithValue("@CityOnLeave", model.CityOnLeave ?? string.Empty);

                await conn.OpenAsync();
                await cmd.ExecuteNonQueryAsync();

                return RedirectToAction("MyLeaves");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Database error occurred while submitting application: " + ex.Message);
                await PopulateLeaveBalancesAndManagersAsync(userPNo);
                return View(model);
            }
        }

        public async Task<IActionResult> MyLeaves()
        {
            string? userPNo = HttpContext.Session.GetString("PNo");
            if (string.IsNullOrEmpty(userPNo)) return RedirectToAction("Login");

            var list = new List<LeaveApplication>();

            await using var conn = new SqlConnection(GetConnectionString());
            string query = "SELECT * FROM LeaveApplications WHERE TRIM(ApplicantPNo) = TRIM(@PNo) ORDER BY Id DESC";
            await using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@PNo", userPNo);

            await conn.OpenAsync();
            await using var dr = await cmd.ExecuteReaderAsync();
            while (await dr.ReadAsync())
            {
                list.Add(MapLeaveApplication(dr));
            }

            return View(list);
        }

        // ==========================================
        // 4. QUEUES (Executive & Admin Approvals)
        // ==========================================

        public async Task<IActionResult> ExecutiveLeaveQueue(string filter = "Pending")
        {
            string? role = HttpContext.Session.GetString("Role");
            if (role != "General Manager" && role != "Deputy GM") return RedirectToAction("Dashboard");

            string? currentPNo = HttpContext.Session.GetString("PNo");
            if (string.IsNullOrEmpty(currentPNo)) return RedirectToAction("Login");

            var list = new List<LeaveApplication>();

            await using var conn = new SqlConnection(GetConnectionString());
            string query = "";

            // Filter based on tab selection
            if (filter == "History")
            {
                query = @"SELECT * FROM LeaveApplications 
                         WHERE TRIM(ManagerPNo) = TRIM(@ManagerPNo) 
                           AND Status IN ('Approved', 'Rejected') 
                         ORDER BY Id DESC";
            }
            else // Default: Pending
            {
                query = @"SELECT * FROM LeaveApplications 
                         WHERE TRIM(ManagerPNo) = TRIM(@ManagerPNo) 
                           AND Status = 'Pending Executive Review' 
                         ORDER BY Id DESC";
            }

            await using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@ManagerPNo", currentPNo);

            await conn.OpenAsync();
            await using var dr = await cmd.ExecuteReaderAsync();
            while (await dr.ReadAsync())
            {
                list.Add(MapLeaveApplication(dr));
            }

            ViewBag.CurrentFilter = filter;
            return View(list);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExecutiveProcessLeave(int id, string actionType, string executiveRemarks)
        {
            string? role = HttpContext.Session.GetString("Role");
            if (role != "General Manager" && role != "Deputy GM") return RedirectToAction("Dashboard");

            bool isApprove = actionType.Equals("Approve", StringComparison.OrdinalIgnoreCase);

            // Sirf Reject karte waqt Reason / Remarks mandatory hai
            if (!isApprove && string.IsNullOrWhiteSpace(executiveRemarks))
            {
                TempData["Error"] = "Reason is mandatory when rejecting an application.";
                return RedirectToAction("ExecutiveLeaveQueue");
            }

            string newStatus = isApprove ? "Recommended by GM" : "Rejected by GM";

            await using var conn = new SqlConnection(GetConnectionString());
            string query = @"UPDATE LeaveApplications 
                            SET Status = @Status, 
                                ExecutiveRemarks = @Remarks 
                            WHERE Id = @Id";

            await using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Status", newStatus);
            cmd.Parameters.AddWithValue("@Remarks", string.IsNullOrWhiteSpace(executiveRemarks) ? DBNull.Value : executiveRemarks.Trim());
            cmd.Parameters.AddWithValue("@Id", id);

            await conn.OpenAsync();
            await cmd.ExecuteNonQueryAsync();

            return RedirectToAction("ExecutiveLeaveQueue");
        }

        public async Task<IActionResult> AdminLeaveQueue(string filter = "Pending")
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Dashboard");

            var list = new List<LeaveApplication>();

            await using var conn = new SqlConnection(GetConnectionString());
            string leaveQuery = "";

            // Filter based on tab selection
            if (filter == "History")
            {
                leaveQuery = @"SELECT * FROM LeaveApplications 
                              WHERE Status IN ('Approved', 'Rejected') 
                              ORDER BY Id DESC";
            }
            else // Default: Pending
            {
                leaveQuery = @"SELECT * FROM LeaveApplications 
                              WHERE Status IN ('Recommended by GM', 'Rejected by GM') 
                              ORDER BY Id DESC";
            }

            await using var cmd = new SqlCommand(leaveQuery, conn);

            await conn.OpenAsync();
            await using (var dr = await cmd.ExecuteReaderAsync())
            {
                while (await dr.ReadAsync())
                {
                    list.Add(MapLeaveApplication(dr));
                }
            }

            ViewBag.Managers = await GetManagersAsync(conn);
            ViewBag.CurrentFilter = filter;
            return View(list);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminFinalProcessLeave(int id, string actionType, string adminRemarks)
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Dashboard");

            bool isApprove = actionType.Equals("Approve", StringComparison.OrdinalIgnoreCase);

            // Sirf Reject karte waqt Reason / Remarks mandatory hai
            if (!isApprove && string.IsNullOrWhiteSpace(adminRemarks))
            {
                TempData["Error"] = "Reason is mandatory when rejecting an application.";
                return RedirectToAction("AdminLeaveQueue");
            }

            string finalStatus = isApprove ? "Approved" : "Rejected";

            await using var conn = new SqlConnection(GetConnectionString());
            string query = @"UPDATE LeaveApplications 
                            SET Status = @Status, 
                                AdminRemarks = @Remarks 
                            WHERE Id = @Id";

            await using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Status", finalStatus);
            cmd.Parameters.AddWithValue("@Remarks", string.IsNullOrWhiteSpace(adminRemarks) ? DBNull.Value : adminRemarks.Trim());
            cmd.Parameters.AddWithValue("@Id", id);

            await conn.OpenAsync();
            await cmd.ExecuteNonQueryAsync();

            return RedirectToAction("AdminLeaveQueue");
        }

        // ==========================================
        // 5. USER MANAGEMENT (Admin Only)
        // ==========================================

        public async Task<IActionResult> ManageUsers()
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Dashboard");

            var usersList = new List<UserViewModel>();
            await using var conn = new SqlConnection(GetConnectionString());

            string query = @"
                SELECT u.PNo, u.FullName, u.Email, u.Role,
                       ISNULL(b.CasualQuota, 5) AS CasualQuota,
                       ISNULL(b.MedicalQuota, 5) AS MedicalQuota,
                       ISNULL(b.AnnualQuota, 5) AS AnnualQuota
                FROM Users u
                LEFT JOIN UserLeaveBalances b ON TRIM(u.PNo) = TRIM(b.PNo)
                ORDER BY u.PNo ASC";

            await using var cmd = new SqlCommand(query, conn);

            await conn.OpenAsync();
            await using var dr = await cmd.ExecuteReaderAsync();
            while (await dr.ReadAsync())
            {
                usersList.Add(new UserViewModel
                {
                    PNo = dr["PNo"].ToString()!,
                    FullName = dr["FullName"].ToString()!,
                    Email = dr["Email"] != DBNull.Value ? dr["Email"].ToString()! : string.Empty,
                    Role = dr["Role"].ToString()!,
                    CasualQuota = Convert.ToInt32(dr["CasualQuota"]),
                    MedicalQuota = Convert.ToInt32(dr["MedicalQuota"]),
                    AnnualQuota = Convert.ToInt32(dr["AnnualQuota"])
                });
            }

            return View(usersList);
        }

        [HttpGet]
        public async Task<IActionResult> AssignLeaveBalances()
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Dashboard");

            var usersList = new List<UserViewModel>();
            await using var conn = new SqlConnection(GetConnectionString());
            await conn.OpenAsync();

            string query = @"
                SELECT u.PNo, u.FullName, u.Email, u.Role,
                       ISNULL(b.CasualQuota, 5) AS CasualQuota,
                       ISNULL(b.MedicalQuota, 5) AS MedicalQuota,
                       ISNULL(b.AnnualQuota, 5) AS AnnualQuota,
                       b.UpdatedAt
                FROM Users u
                LEFT JOIN UserLeaveBalances b ON TRIM(u.PNo) = TRIM(b.PNo)
                WHERE u.Role = 'Employee'
                ORDER BY u.PNo ASC";

            await using var cmd = new SqlCommand(query, conn);
            await using var dr = await cmd.ExecuteReaderAsync();

            var usersRaw = new List<dynamic>();
            while (await dr.ReadAsync())
            {
                usersRaw.Add(new
                {
                    PNo = dr["PNo"].ToString()!,
                    FullName = dr["FullName"].ToString()!,
                    Email = dr["Email"] != DBNull.Value ? dr["Email"].ToString()! : string.Empty,
                    Role = dr["Role"].ToString()!,
                    CasualQuota = Convert.ToInt32(dr["CasualQuota"]),
                    MedicalQuota = Convert.ToInt32(dr["MedicalQuota"]),
                    AnnualQuota = Convert.ToInt32(dr["AnnualQuota"]),
                    UpdatedAt = dr["UpdatedAt"] != DBNull.Value ? Convert.ToDateTime(dr["UpdatedAt"]) : (DateTime?)null
                });
            }
            await dr.CloseAsync();

            foreach (var u in usersRaw)
            {
                int usedCasual = 0, usedMedical = 0, usedAnnual = 0;
                DateTime filterDate = u.UpdatedAt ?? new DateTime(1900, 1, 1);

                string countQuery = @"SELECT LeaveType, ISNULL(SUM(DATEDIFF(day, StartDate, EndDate) + 1), 0) AS TotalDays
                                      FROM LeaveApplications 
                                      WHERE TRIM(ApplicantPNo) = TRIM(@PNo) 
                                        AND Status LIKE '%Approv%'
                                        AND AppliedDate >= @UpdatedAt
                                      GROUP BY LeaveType";

                await using (var countCmd = new SqlCommand(countQuery, conn))
                {
                    countCmd.Parameters.AddWithValue("@PNo", u.PNo);
                    countCmd.Parameters.AddWithValue("@UpdatedAt", filterDate);
                    await using var cdr = await countCmd.ExecuteReaderAsync();
                    while (await cdr.ReadAsync())
                    {
                        string type = cdr["LeaveType"]?.ToString() ?? string.Empty;
                        int days = Convert.ToInt32(cdr["TotalDays"]);

                        if (type.Contains("Casual", StringComparison.OrdinalIgnoreCase)) usedCasual = days;
                        else if (type.Contains("Medical", StringComparison.OrdinalIgnoreCase)) usedMedical = days;
                        else if (type.Contains("Annual", StringComparison.OrdinalIgnoreCase)) usedAnnual = days;
                    }
                }

                usersList.Add(new UserViewModel
                {
                    PNo = u.PNo,
                    FullName = u.FullName,
                    Email = u.Email,
                    Role = u.Role,
                    CasualQuota = u.CasualQuota - usedCasual,
                    MedicalQuota = u.MedicalQuota - usedMedical,
                    AnnualQuota = u.AnnualQuota - usedAnnual
                });
            }

            return View(usersList);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddUser(string pno, string fullName, string email, string password, string role)
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Dashboard");

            if (string.IsNullOrWhiteSpace(pno) || string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(role))
            {
                TempData["Error"] = "All required fields (P.No, Full Name, Password, and Role) must be filled.";
                return RedirectToAction("ManageUsers");
            }

            try
            {
                await using var conn = new SqlConnection(GetConnectionString());
                await conn.OpenAsync();

                // Check for duplicate PNo
                string checkQuery = "SELECT COUNT(1) FROM Users WHERE TRIM(PNo) = TRIM(@PNo)";
                await using (var checkCmd = new SqlCommand(checkQuery, conn))
                {
                    checkCmd.Parameters.AddWithValue("@PNo", pno.Trim());
                    int existingCount = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                    if (existingCount > 0)
                    {
                        TempData["Error"] = $"A user with P.No '{pno.Trim()}' already exists!";
                        return RedirectToAction("ManageUsers");
                    }
                }

                string hashedPassword = BCrypt.Net.BCrypt.HashPassword(password);

                string insertQuery = @"INSERT INTO Users (PNo, FullName, Email, Password, Role) 
                                      VALUES (@PNo, @FullName, @Email, @Password, @Role)";

                await using (var cmd = new SqlCommand(insertQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@PNo", pno.Trim());
                    cmd.Parameters.AddWithValue("@FullName", fullName.Trim());
                    cmd.Parameters.AddWithValue("@Email", string.IsNullOrWhiteSpace(email) ? DBNull.Value : email.Trim());
                    cmd.Parameters.AddWithValue("@Password", hashedPassword);
                    cmd.Parameters.AddWithValue("@Role", role.Trim());

                    await cmd.ExecuteNonQueryAsync();
                }

                TempData["Success"] = $"User '{fullName.Trim()}' (P.No: {pno.Trim()}) added successfully!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error adding user: " + ex.Message;
            }

            return RedirectToAction("ManageUsers");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditUser(string pno, string fullName, string email, string role)
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Dashboard");

            await using var conn = new SqlConnection(GetConnectionString());
            string query = "UPDATE Users SET FullName = @FullName, Email = @Email, Role = @Role WHERE TRIM(PNo) = TRIM(@PNo)";
            await using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@FullName", fullName.Trim());
            cmd.Parameters.AddWithValue("@Email", email ?? string.Empty);
            cmd.Parameters.AddWithValue("@Role", role);
            cmd.Parameters.AddWithValue("@PNo", pno.Trim());

            await conn.OpenAsync();
            await cmd.ExecuteNonQueryAsync();

            return RedirectToAction("ManageUsers");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignLeaveBalance()
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
            {
                return RedirectToAction("Login", "Account");
            }

            string pno = Request.Form["pno"].ToString();
            int.TryParse(Request.Form["casualQuota"], out int casualQuota);
            int.TryParse(Request.Form["medicalQuota"], out int medicalQuota);
            int.TryParse(Request.Form["annualQuota"], out int annualQuota);

            if (string.IsNullOrWhiteSpace(pno))
            {
                TempData["Error"] = "User P.No missing!";
                return RedirectToAction("AssignLeaveBalances");
            }

            try
            {
                await using (var conn = new SqlConnection(GetConnectionString()))
                {
                    await conn.OpenAsync();

                    string query = @"
                        IF EXISTS (SELECT 1 FROM UserLeaveBalances WHERE TRIM(PNo) = TRIM(@PNo))
                        BEGIN
                            UPDATE UserLeaveBalances 
                            SET CasualQuota = @Casual, 
                                MedicalQuota = @Medical, 
                                AnnualQuota = @Annual,
                                UpdatedAt = GETDATE()
                            WHERE TRIM(PNo) = TRIM(@PNo)
                        END
                        ELSE
                        BEGIN
                            INSERT INTO UserLeaveBalances (PNo, CasualQuota, MedicalQuota, AnnualQuota, UpdatedAt) 
                            VALUES (TRIM(@PNo), @Casual, @Medical, @Annual, GETDATE())
                        END";

                    await using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@PNo", pno.Trim());
                        cmd.Parameters.AddWithValue("@Casual", casualQuota);
                        cmd.Parameters.AddWithValue("@Medical", medicalQuota);
                        cmd.Parameters.AddWithValue("@Annual", annualQuota);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                TempData["Success"] = "Leave Quotas successfully saved/updated!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Database Error: " + ex.Message;
            }

            return RedirectToAction("AssignLeaveBalances");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(string pno)
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Dashboard");

            await using var conn = new SqlConnection(GetConnectionString());
            string query = "DELETE FROM Users WHERE TRIM(PNo) = TRIM(@PNo)";
            await using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@PNo", pno.Trim());

            await conn.OpenAsync();
            await cmd.ExecuteNonQueryAsync();

            return RedirectToAction("ManageUsers");
        }

        // ==========================================
        // PRIVATE HELPER METHODS
        // ==========================================

        private async Task PopulateLeaveBalancesAndManagersAsync(string userPNo)
        {
            int maxCasual = 5, maxMedical = 5, maxAnnual = 5;
            int usedCasual = 0, usedMedical = 0, usedAnnual = 0;
            DateTime filterDate = new DateTime(1900, 1, 1);

            await using var conn = new SqlConnection(GetConnectionString());
            await conn.OpenAsync();

            string quotaQuery = "SELECT CasualQuota, MedicalQuota, AnnualQuota, UpdatedAt FROM UserLeaveBalances WHERE TRIM(PNo) = TRIM(@PNo)";
            await using (var cmdQuota = new SqlCommand(quotaQuery, conn))
            {
                cmdQuota.Parameters.AddWithValue("@PNo", userPNo);
                await using var qdr = await cmdQuota.ExecuteReaderAsync();
                if (await qdr.ReadAsync())
                {
                    maxCasual = Convert.ToInt32(qdr["CasualQuota"]);
                    maxMedical = Convert.ToInt32(qdr["MedicalQuota"]);
                    maxAnnual = Convert.ToInt32(qdr["AnnualQuota"]);
                    if (qdr["UpdatedAt"] != DBNull.Value)
                    {
                        filterDate = Convert.ToDateTime(qdr["UpdatedAt"]);
                    }
                }
            }

            string countQuery = @"SELECT LeaveType, ISNULL(SUM(DATEDIFF(day, StartDate, EndDate) + 1), 0) AS TotalDays
                                  FROM LeaveApplications 
                                  WHERE TRIM(ApplicantPNo) = TRIM(@PNo) 
                                    AND Status LIKE '%Approv%'
                                    AND AppliedDate >= @UpdatedAt
                                  GROUP BY LeaveType";

            await using (var cmd = new SqlCommand(countQuery, conn))
            {
                cmd.Parameters.AddWithValue("@PNo", userPNo);
                cmd.Parameters.AddWithValue("@UpdatedAt", filterDate);
                await using var dr = await cmd.ExecuteReaderAsync();
                while (await dr.ReadAsync())
                {
                    string type = dr["LeaveType"]?.ToString() ?? string.Empty;
                    int days = Convert.ToInt32(dr["TotalDays"]);

                    if (type.Contains("Casual", StringComparison.OrdinalIgnoreCase)) usedCasual = days;
                    else if (type.Contains("Medical", StringComparison.OrdinalIgnoreCase)) usedMedical = days;
                    else if (type.Contains("Annual", StringComparison.OrdinalIgnoreCase)) usedAnnual = days;
                }
            }

            ViewBag.CasualBalance = maxCasual - usedCasual;
            ViewBag.MedicalBalance = maxMedical - usedMedical;
            ViewBag.AnnualBalance = maxAnnual - usedAnnual;

            ViewBag.TotalAvailable = (maxCasual - usedCasual) + (maxMedical - usedMedical) + (maxAnnual - usedAnnual);
            ViewBag.Managers = await GetManagersAsync(conn);
        }

        private async Task<List<UserViewModel>> GetManagersAsync(SqlConnection? existingConn = null)
        {
            var managers = new List<UserViewModel>();
            bool closeConn = false;

            if (existingConn == null)
            {
                existingConn = new SqlConnection(GetConnectionString());
                await existingConn.OpenAsync();
                closeConn = true;
            }
            else if (existingConn.State != System.Data.ConnectionState.Open)
            {
                await existingConn.OpenAsync();
            }

            try
            {
                string managerQuery = "SELECT PNo, FullName, Role FROM Users WHERE Role IN ('General Manager', 'Deputy GM')";
                await using var cmd = new SqlCommand(managerQuery, existingConn);
                await using var dr = await cmd.ExecuteReaderAsync();
                while (await dr.ReadAsync())
                {
                    managers.Add(new UserViewModel
                    {
                        PNo = dr["PNo"].ToString()!,
                        FullName = dr["FullName"].ToString()!,
                        Role = dr["Role"].ToString()!
                    });
                }
            }
            finally
            {
                if (closeConn) await existingConn.DisposeAsync();
            }

            return managers;
        }

        private static LeaveApplication MapLeaveApplication(SqlDataReader dr)
        {
            return new LeaveApplication
            {
                Id = Convert.ToInt32(dr["Id"]),
                ApplicantPNo = dr["ApplicantPNo"]?.ToString() ?? string.Empty,
                ApplicantName = dr["ApplicantName"] != DBNull.Value ? dr["ApplicantName"].ToString() : string.Empty,
                StartDate = Convert.ToDateTime(dr["StartDate"]),
                EndDate = Convert.ToDateTime(dr["EndDate"]),
                LeaveType = dr["LeaveType"]?.ToString() ?? string.Empty,
                ManagerPNo = dr["ManagerPNo"] != DBNull.Value ? dr["ManagerPNo"].ToString() : string.Empty,
                AddressOnLeave = dr["AddressOnLeave"] != DBNull.Value ? dr["AddressOnLeave"].ToString() : string.Empty,
                Reason = dr["Reason"] != DBNull.Value ? dr["Reason"].ToString() : string.Empty,
                ContactNo = dr["ContactNo"] != DBNull.Value ? dr["ContactNo"].ToString() : string.Empty,
                CityOnLeave = dr["CityOnLeave"] != DBNull.Value ? dr["CityOnLeave"].ToString() : string.Empty,
                Status = dr["Status"]?.ToString() ?? string.Empty,
                ExecutiveRemarks = dr.HasColumn("ExecutiveRemarks") && dr["ExecutiveRemarks"] != DBNull.Value ? dr["ExecutiveRemarks"].ToString() : string.Empty,
                AdminRemarks = dr.HasColumn("AdminRemarks") && dr["AdminRemarks"] != DBNull.Value ? dr["AdminRemarks"].ToString() : string.Empty
            };
        }
    }

    public static class SqlDataReaderExtensions
    {
        public static bool HasColumn(this SqlDataReader reader, string columnName)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}