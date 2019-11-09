﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using NSwag.Annotations;
using SiteServer.CMS.Core;
using SiteServer.CMS.Core.Office;
using SiteServer.CMS.DataCache;
using SiteServer.CMS.Model;
using SiteServer.CMS.Plugin.Impl;
using SiteServer.Utils;
using SiteServer.Utils.Enumerations;

namespace SiteServer.API.Controllers.Pages.Settings.Admin
{
    [OpenApiIgnore]
    [RoutePrefix("pages/settings/admin")]
    public class PagesAdminController : ApiController
    {
        private const string Route = "";
        private const string RoutePermissions = "permissions/{adminId:int}";
        private const string RouteLock = "actions/lock";
        private const string RouteUnLock = "actions/unLock";
        private const string RouteImport = "actions/import";
        private const string RouteExport = "actions/export";

        [HttpGet, Route(Route)]
        public async Task<IHttpActionResult> GetConfig()
        {
            try
            {
                var request = new AuthenticatedRequest();
                if (!request.IsAdminLoggin ||
                    !request.AdminPermissionsImpl.HasSystemPermissions(ConfigManager.SettingsPermissions.Admin))
                {
                    return Unauthorized();
                }

                var roles = new List<KeyValuePair<string, string>>();

                var roleNameList = request.AdminPermissionsImpl.IsConsoleAdministrator ? DataProvider.RoleDao.GetRoleNameList() : DataProvider.RoleDao.GetRoleNameListByCreatorUserName(request.AdminName);

                var predefinedRoles = EPredefinedRoleUtils.GetAllPredefinedRoleName();
                foreach (var predefinedRole in predefinedRoles)
                {
                    roles.Add(new KeyValuePair<string, string>(predefinedRole, EPredefinedRoleUtils.GetText(EPredefinedRoleUtils.GetEnumType(predefinedRole))));
                }
                foreach (var roleName in roleNameList)
                {
                    if (!predefinedRoles.Contains(roleName))
                    {
                        roles.Add(new KeyValuePair<string, string>(roleName, roleName));
                    }
                }

                var role = request.GetQueryString("role");
                var order = request.GetQueryString("order");
                var lastActivityDate = request.GetQueryInt("lastActivityDate");
                var keyword = request.GetQueryString("keyword");
                var offset = request.GetQueryInt("offset");
                var limit = request.GetQueryInt("limit");

                var isSuperAdmin = request.AdminPermissions.IsSuperAdmin();
                var creatorUserName = isSuperAdmin ? string.Empty : request.AdminName;
                var count = await DataProvider.AdministratorDao.GetCountAsync(creatorUserName, role, lastActivityDate, keyword);
                var administratorInfoList = await DataProvider.AdministratorDao.GetAdministratorsAsync(creatorUserName, role, order, lastActivityDate, keyword, offset, limit);
                var administrators = new List<object>();
                foreach (var administratorInfo in administratorInfoList)
                {
                    administrators.Add(new
                    {
                        administratorInfo.Id,
                        administratorInfo.AvatarUrl,
                        administratorInfo.UserName,
                        DisplayName = string.IsNullOrEmpty(administratorInfo.DisplayName)
                            ? administratorInfo.UserName
                            : administratorInfo.DisplayName,
                        administratorInfo.Mobile,
                        administratorInfo.LastActivityDate,
                        administratorInfo.CountOfLogin,
                        administratorInfo.Locked,
                        Roles = AdminManager.GetRoles(administratorInfo.UserName)
                    });
                }

                return Ok(new
                {
                    Value = administrators,
                    Count = count,
                    Roles = roles,
                    IsSuperAdmin = request.AdminPermissions.IsSuperAdmin(),
                    request.AdminId
                });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        [HttpGet, Route(RoutePermissions)]
        public async Task<IHttpActionResult> GetPermissions(int adminId)
        {
            try
            {
                var request = new AuthenticatedRequest();
                if (!request.IsAdminLoggin ||
                    !request.AdminPermissionsImpl.HasSystemPermissions(ConfigManager.SettingsPermissions.Admin))
                {
                    return Unauthorized();
                }

                if (!request.AdminPermissions.IsSuperAdmin())
                {
                    return Unauthorized();
                }

                var roles = DataProvider.RoleDao.GetRoleNameList();
                var allSites = await SiteManager.GetSiteListAsync();

                var adminInfo = await AdminManager.GetByUserIdAsync(adminId);
                var adminRoles = DataProvider.AdministratorsInRolesDao.GetRolesForUser(adminInfo.UserName);
                string adminLevel;
                var checkedSites = new List<int>();
                var checkedRoles = new List<string>();
                if (EPredefinedRoleUtils.IsConsoleAdministrator(adminRoles))
                {
                    adminLevel = "SuperAdmin";
                }
                else if (EPredefinedRoleUtils.IsSystemAdministrator(adminRoles))
                {
                    adminLevel = "SiteAdmin";
                    checkedSites = TranslateUtils.StringCollectionToIntList(adminInfo.SiteIdCollection);
                }
                else
                {
                    adminLevel = "Admin";
                    foreach (var role in roles)
                    {
                        if (!checkedRoles.Contains(role) && !EPredefinedRoleUtils.IsPredefinedRole(role) && adminRoles.Contains(role))
                        {
                            checkedRoles.Add(role);
                        }
                    }
                }

                return Ok(new
                {
                    Value = true,
                    Roles = roles,
                    AllSites = allSites,
                    AdminLevel = adminLevel,
                    CheckedSites = checkedSites,
                    CheckedRoles = checkedRoles
                });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        [HttpPost, Route(RoutePermissions)]
        public async Task<IHttpActionResult> SavePermissions(int adminId)
        {
            try
            {
                var request = new AuthenticatedRequest();
                if (!request.IsAdminLoggin ||
                    !request.AdminPermissionsImpl.HasSystemPermissions(ConfigManager.SettingsPermissions.Admin))
                {
                    return Unauthorized();
                }

                if (!request.AdminPermissions.IsSuperAdmin())
                {
                    return Unauthorized();
                }

                var adminLevel = request.GetPostString("adminLevel");
                var checkedSites = request.GetPostObject<List<int>>("checkedSites");
                var checkedRoles = request.GetPostObject<List<string>>("checkedRoles");

                var adminInfo = await AdminManager.GetByUserIdAsync(adminId);

                DataProvider.AdministratorsInRolesDao.RemoveUser(adminInfo.UserName);
                if (adminLevel == "SuperAdmin")
                {
                    await DataProvider.AdministratorsInRolesDao.AddUserToRoleAsync(adminInfo.UserName, EPredefinedRoleUtils.GetValue(EPredefinedRole.ConsoleAdministrator));
                }
                else if (adminLevel == "SiteAdmin")
                {
                    await DataProvider.AdministratorsInRolesDao.AddUserToRoleAsync(adminInfo.UserName, EPredefinedRoleUtils.GetValue(EPredefinedRole.SystemAdministrator));
                }
                else
                {
                    await DataProvider.AdministratorsInRolesDao.AddUserToRoleAsync(adminInfo.UserName, EPredefinedRoleUtils.GetValue(EPredefinedRole.Administrator));
                    await DataProvider.AdministratorsInRolesDao.AddUserToRolesAsync(adminInfo.UserName, checkedRoles.ToArray());
                }

                await DataProvider.AdministratorDao.UpdateSiteIdCollectionAsync(adminInfo,
                    adminLevel == "SiteAdmin"
                        ? TranslateUtils.ObjectCollectionToString(checkedSites)
                        : string.Empty);

                PermissionsImpl.ClearAllCache();

                await request.AddAdminLogAsync("设置管理员权限", $"管理员:{adminInfo.UserName}");

                return Ok(new
                {
                    Value = true,
                    Roles = AdminManager.GetRoles(adminInfo.UserName)
                });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        [HttpDelete, Route(Route)]
        public async Task<IHttpActionResult> Delete()
        {
            try
            {
                var request = new AuthenticatedRequest();
                if (!request.IsAdminLoggin ||
                    !request.AdminPermissionsImpl.HasSystemPermissions(ConfigManager.SettingsPermissions.Admin))
                {
                    return Unauthorized();
                }

                var id = request.GetPostInt("id");

                var adminInfo = await AdminManager.GetByUserIdAsync(id);
                DataProvider.AdministratorsInRolesDao.RemoveUser(adminInfo.UserName);
                await DataProvider.AdministratorDao.DeleteAsync(adminInfo);

                await request.AddAdminLogAsync("删除管理员", $"管理员:{adminInfo.UserName}");

                return Ok(new
                {
                    Value = true
                });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        [HttpPost, Route(RouteLock)]
        public async Task<IHttpActionResult> Lock()
        {
            try
            {
                var request = new AuthenticatedRequest();
                if (!request.IsAdminLoggin ||
                    !request.AdminPermissionsImpl.HasSystemPermissions(ConfigManager.SettingsPermissions.Admin))
                {
                    return Unauthorized();
                }

                var id = request.GetPostInt("id");

                var adminInfo = await AdminManager.GetByUserIdAsync(id);

                await DataProvider.AdministratorDao.LockAsync(new List<string>
                {
                    adminInfo.UserName
                });

                await request.AddAdminLogAsync("锁定管理员", $"管理员:{adminInfo.UserName}");

                return Ok(new
                {
                    Value = true
                });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        [HttpPost, Route(RouteUnLock)]
        public async Task<IHttpActionResult> UnLock()
        {
            try
            {
                var request = new AuthenticatedRequest();
                if (!request.IsAdminLoggin ||
                    !request.AdminPermissionsImpl.HasSystemPermissions(ConfigManager.SettingsPermissions.Admin))
                {
                    return Unauthorized();
                }

                var id = request.GetPostInt("id");

                var adminInfo = await AdminManager.GetByUserIdAsync(id);

                await DataProvider.AdministratorDao.UnLockAsync(new List<string>
                {
                    adminInfo.UserName
                });

                await request.AddAdminLogAsync("解锁管理员", $"管理员:{adminInfo.UserName}");

                return Ok(new
                {
                    Value = true
                });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        [HttpPost, Route(RouteImport)]
        public async Task<IHttpActionResult> Import()
        {
            try
            {
                var request = new AuthenticatedRequest();
                if (!request.IsAdminLoggin ||
                    !request.AdminPermissionsImpl.HasSystemPermissions(ConfigManager.SettingsPermissions.Admin))
                {
                    return Unauthorized();
                }

                var fileName = request.HttpRequest["fileName"];
                var fileCount = request.HttpRequest.Files.Count;
                if (fileCount == 0)
                {
                    return BadRequest("请选择有效的文件上传");
                }

                var file = request.HttpRequest.Files[0];
                if (string.IsNullOrEmpty(fileName)) fileName = Path.GetFileName(file.FileName);

                var sExt = PathUtils.GetExtension(fileName);
                if (!StringUtils.EqualsIgnoreCase(sExt, ".xlsx"))
                {
                    return BadRequest("导入文件为Excel格式，请选择有效的文件上传");
                }

                var filePath = PathUtils.GetTemporaryFilesPath(fileName);
                DirectoryUtils.CreateDirectoryIfNotExists(filePath);
                file.SaveAs(filePath);

                var errorMessage = string.Empty;
                var success = 0;
                var failure = 0;

                var sheet = ExcelUtils.GetDataTable(filePath);
                if (sheet != null)
                {
                    for (var i = 1; i < sheet.Rows.Count; i++) //行
                    {
                        if (i == 1) continue;

                        var row = sheet.Rows[i];

                        var userName = row[0].ToString().Trim();
                        var password = row[1].ToString().Trim();
                        var displayName = row[2].ToString().Trim();
                        var mobile = row[3].ToString().Trim();
                        var email = row[4].ToString().Trim();

                        if (!string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(password))
                        {
                            var (isValid, message) = await DataProvider.AdministratorDao.InsertAsync(new Administrator
                            {
                                UserName = userName,
                                DisplayName = displayName,
                                Mobile = mobile,
                                Email = email
                            }, password);
                            if (!isValid)
                            {
                                failure++;
                                errorMessage = message;
                            }
                            else
                            {
                                success++;
                            }
                        }
                        else
                        {
                            failure++;
                        }
                    }
                }

                return Ok(new
                {
                    Value = true,
                    Success = success,
                    Failure = failure,
                    ErrorMessage = errorMessage
                });
            }
            catch (Exception ex)
            {
                LogUtils.AddErrorLog(ex);
                return InternalServerError(ex);
            }
        }

        [HttpPost, Route(RouteExport)]
        public async Task<IHttpActionResult> Export()
        {
            try
            {
                var request = new AuthenticatedRequest();
                if (!request.IsAdminLoggin ||
                    !request.AdminPermissionsImpl.HasSystemPermissions(ConfigManager.SettingsPermissions.Admin))
                {
                    return Unauthorized();
                }

                const string fileName = "administrators.csv";
                var filePath = PathUtils.GetTemporaryFilesPath(fileName);

                await ExcelObject.CreateExcelFileForAdministratorsAsync(filePath);
                var downloadUrl = PageUtils.GetRootUrlByPhysicalPath(filePath);

                return Ok(new
                {
                    Value = downloadUrl
                });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
    }
}