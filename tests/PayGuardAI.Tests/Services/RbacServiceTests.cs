using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;
using PayGuardAI.Data;
using PayGuardAI.Data.Services;

namespace PayGuardAI.Tests.Services;

public class RbacServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly RbacService _service;
    private const string TenantId = "test-tenant";

    public RbacServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();

        var logger = Mock.Of<ILogger<RbacService>>();
        _service = new RbacService(_db, logger);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    // ── System Roles ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetRoles_ShouldSeedSystemRoles_WhenNoneExist()
    {
        var roles = await _service.GetRolesAsync(TenantId);

        roles.Should().HaveCount(3);
        roles.Should().Contain(r => r.Name == "Reviewer" && r.IsSystem);
        roles.Should().Contain(r => r.Name == "Manager" && r.IsSystem);
        roles.Should().Contain(r => r.Name == "Admin" && r.IsSystem);
    }

    [Fact]
    public async Task GetRoles_ShouldNotDuplicateSystemRoles_OnSecondCall()
    {
        await _service.GetRolesAsync(TenantId);
        var roles = await _service.GetRolesAsync(TenantId);

        roles.Count(r => r.IsSystem).Should().Be(3);
    }

    [Fact]
    public async Task SystemRole_Admin_ShouldHaveAllPermissions()
    {
        var roles = await _service.GetRolesAsync(TenantId);
        var admin = roles.First(r => r.Name == "Admin");

        var allPermissions = Enum.GetValues<Permission>();
        admin.GetPermissions().Should().BeEquivalentTo(allPermissions);
    }

    [Fact]
    public async Task SystemRole_Reviewer_ShouldHaveLimitedPermissions()
    {
        var roles = await _service.GetRolesAsync(TenantId);
        var reviewer = roles.First(r => r.Name == "Reviewer");

        var perms = reviewer.GetPermissions();
        perms.Should().Contain(Permission.ViewTransactions);
        perms.Should().Contain(Permission.ReviewTransactions);
        perms.Should().Contain(Permission.ViewReports);
        perms.Should().NotContain(Permission.ManageRoles);
        perms.Should().NotContain(Permission.ManageTeam);
    }

    // ── Custom Role CRUD ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateRole_ShouldPersistWithPermissions()
    {
        var permissions = new[] { Permission.ViewTransactions, Permission.ManageRules };
        var role = await _service.CreateRoleAsync(TenantId, "Analyst", "Custom analyst role",
            permissions, "admin@test.com");

        role.Name.Should().Be("Analyst");
        role.Description.Should().Be("Custom analyst role");
        role.IsSystem.Should().BeFalse();
        role.CreatedBy.Should().Be("admin@test.com");
        role.GetPermissions().Should().BeEquivalentTo(permissions);
    }

    [Fact]
    public async Task CreateRole_ShouldCreateAuditLog()
    {
        await _service.CreateRoleAsync(TenantId, "Auditor", null,
            [Permission.ViewAuditLog], "admin@test.com");

        var logs = await _db.AuditLogs.Where(a => a.Action == "ROLE_CREATED").ToListAsync();
        logs.Should().ContainSingle();
        logs[0].PerformedBy.Should().Be("admin@test.com");
        logs[0].Notes.Should().Contain("Auditor");
    }

    [Fact]
    public async Task CreateRole_ShouldReject_DuplicateName()
    {
        await _service.CreateRoleAsync(TenantId, "Analyst", null,
            [Permission.ViewTransactions], "admin@test.com");

        var act = () => _service.CreateRoleAsync(TenantId, "Analyst", null,
            [Permission.ViewReports], "admin@test.com");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task UpdateRole_ShouldModifyPermissions()
    {
        var role = await _service.CreateRoleAsync(TenantId, "Analyst", null,
            [Permission.ViewTransactions], "admin@test.com");

        var updated = await _service.UpdateRoleAsync(role.Id, "Senior Analyst", "Updated desc",
            [Permission.ViewTransactions, Permission.ManageRules, Permission.ViewReports], "admin@test.com");

        updated.Name.Should().Be("Senior Analyst");
        updated.Description.Should().Be("Updated desc");
        updated.GetPermissions().Should().HaveCount(3);
    }

    [Fact]
    public async Task UpdateRole_ShouldCreateAuditLog_WithOldAndNewValues()
    {
        var role = await _service.CreateRoleAsync(TenantId, "Analyst", null,
            [Permission.ViewTransactions], "admin@test.com");

        await _service.UpdateRoleAsync(role.Id, "Senior Analyst", null,
            [Permission.ViewTransactions, Permission.ManageRules], "manager@test.com");

        var logs = await _db.AuditLogs.Where(a => a.Action == "ROLE_UPDATED").ToListAsync();
        logs.Should().ContainSingle();
        logs[0].OldValues.Should().Contain("Analyst");
        logs[0].NewValues.Should().Contain("Senior Analyst");
        logs[0].PerformedBy.Should().Be("manager@test.com");
    }

    [Fact]
    public async Task UpdateRole_ShouldReject_SystemRole()
    {
        await _service.GetRolesAsync(TenantId); // Seed system roles
        var admin = await _service.GetRoleByNameAsync(TenantId, "Admin");

        var act = () => _service.UpdateRoleAsync(admin!.Id, "Super Admin", null,
            [Permission.ViewTransactions], "hacker@test.com");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*System roles*");
    }

    [Fact]
    public async Task DeleteRole_ShouldRemoveFromDatabase()
    {
        var role = await _service.CreateRoleAsync(TenantId, "Temp Role", null,
            [Permission.ViewTransactions], "admin@test.com");

        await _service.DeleteRoleAsync(role.Id, "admin@test.com");

        var found = await _service.GetRoleByIdAsync(role.Id);
        found.Should().BeNull();
    }

    [Fact]
    public async Task DeleteRole_ShouldReject_SystemRole()
    {
        await _service.GetRolesAsync(TenantId);
        var reviewer = await _service.GetRoleByNameAsync(TenantId, "Reviewer");

        var act = () => _service.DeleteRoleAsync(reviewer!.Id, "admin@test.com");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*System roles*");
    }

    [Fact]
    public async Task DeleteRole_ShouldReject_WhenMembersAssigned()
    {
        var role = await _service.CreateRoleAsync(TenantId, "Analyst", null,
            [Permission.ViewTransactions], "admin@test.com");

        _db.TeamMembers.Add(new TeamMember
        {
            TenantId = TenantId,
            Email = "analyst@test.com",
            DisplayName = "Test Analyst",
            Role = "Analyst"
        });
        await _db.SaveChangesAsync();

        var act = () => _service.DeleteRoleAsync(role.Id, "admin@test.com");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*1 team member*");
    }

    [Fact]
    public async Task DeleteRole_ShouldCreateAuditLog()
    {
        var role = await _service.CreateRoleAsync(TenantId, "Temp", null,
            [Permission.ViewTransactions], "admin@test.com");

        await _service.DeleteRoleAsync(role.Id, "admin@test.com");

        var logs = await _db.AuditLogs.Where(a => a.Action == "ROLE_DELETED").ToListAsync();
        logs.Should().ContainSingle();
        logs[0].Notes.Should().Contain("Temp");
    }

    // ── Permission Checks ─────────────────────────────────────────────────

    [Theory]
    [InlineData("Admin", Permission.ManageRoles, true)]
    [InlineData("Admin", Permission.ViewTransactions, true)]
    [InlineData("Manager", Permission.ManageTeam, true)]
    [InlineData("Manager", Permission.ManageRoles, false)]
    [InlineData("Reviewer", Permission.ViewTransactions, true)]
    [InlineData("Reviewer", Permission.ManageRules, false)]
    public async Task HasPermission_ShouldCheckBuiltInRoles(string role, Permission perm, bool expected)
    {
        var result = await _service.HasPermissionAsync(TenantId, role, perm);
        result.Should().Be(expected);
    }

    [Fact]
    public async Task HasPermission_ShouldCheckCustomRole()
    {
        await _service.CreateRoleAsync(TenantId, "Auditor", null,
            [Permission.ViewAuditLog, Permission.ViewReports], "admin@test.com");

        (await _service.HasPermissionAsync(TenantId, "Auditor", Permission.ViewAuditLog))
            .Should().BeTrue();
        (await _service.HasPermissionAsync(TenantId, "Auditor", Permission.ManageRules))
            .Should().BeFalse();
    }

    [Fact]
    public async Task HasPermission_ShouldReturnFalse_ForUnknownRole()
    {
        var result = await _service.HasPermissionAsync(TenantId, "NonExistentRole", Permission.ViewTransactions);
        result.Should().BeFalse();
    }

    // ── Role Assignment ───────────────────────────────────────────────────

    [Fact]
    public async Task AssignRole_ShouldUpdateMemberAndAudit()
    {
        var member = new TeamMember
        {
            TenantId = TenantId,
            Email = "user@test.com",
            DisplayName = "Test User",
            Role = "Reviewer"
        };
        _db.TeamMembers.Add(member);
        await _db.SaveChangesAsync();

        await _service.AssignRoleAsync(member.Id, "Manager", "admin@test.com");

        var updated = await _db.TeamMembers.FindAsync(member.Id);
        updated!.Role.Should().Be("Manager");

        var logs = await _db.AuditLogs.Where(a => a.Action == "MEMBER_ROLE_CHANGED").ToListAsync();
        logs.Should().ContainSingle();
        logs[0].OldValues.Should().Contain("Reviewer");
        logs[0].NewValues.Should().Contain("Manager");
        logs[0].Notes.Should().Contain("Test User");
    }

    [Fact]
    public async Task AssignRole_ShouldNoOp_WhenSameRole()
    {
        var member = new TeamMember
        {
            TenantId = TenantId,
            Email = "user@test.com",
            DisplayName = "Test User",
            Role = "Reviewer"
        };
        _db.TeamMembers.Add(member);
        await _db.SaveChangesAsync();

        await _service.AssignRoleAsync(member.Id, "Reviewer", "admin@test.com");

        var logs = await _db.AuditLogs.Where(a => a.Action == "MEMBER_ROLE_CHANGED").ToListAsync();
        logs.Should().BeEmpty(); // No audit entry for no-op
    }

    // ── Audit Log ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRbacAuditLog_ShouldReturnRoleAndMemberChanges()
    {
        await _service.CreateRoleAsync(TenantId, "Analyst", null,
            [Permission.ViewTransactions], "admin@test.com");

        var member = new TeamMember
        {
            TenantId = TenantId,
            Email = "user@test.com",
            DisplayName = "Test",
            Role = "Reviewer"
        };
        _db.TeamMembers.Add(member);
        await _db.SaveChangesAsync();
        await _service.AssignRoleAsync(member.Id, "Analyst", "admin@test.com");

        var logs = await _service.GetRbacAuditLogAsync(TenantId);
        logs.Should().HaveCount(2); // ROLE_CREATED + MEMBER_ROLE_CHANGED
    }

    // ── Permission Info ───────────────────────────────────────────────────

    [Fact]
    public void GetAllPermissions_ShouldReturnAllDefinedPermissions()
    {
        var infos = _service.GetAllPermissions();
        var allPerms = Enum.GetValues<Permission>();

        infos.Should().HaveCount(allPerms.Length);
        infos.Select(i => i.Permission).Should().BeEquivalentTo(allPerms);
        infos.Should().OnlyContain(i => !string.IsNullOrEmpty(i.Name));
        infos.Should().OnlyContain(i => !string.IsNullOrEmpty(i.Category));
    }

    // ── CustomRole Entity Helpers ─────────────────────────────────────────

    [Fact]
    public void CustomRole_SetAndGetPermissions_ShouldRoundTrip()
    {
        var role = new CustomRole();
        role.SetPermissions([Permission.ViewTransactions, Permission.ManageRules, Permission.ViewReports]);

        var perms = role.GetPermissions();
        perms.Should().HaveCount(3);
        perms.Should().Contain(Permission.ViewTransactions);
        perms.Should().Contain(Permission.ManageRules);
        perms.Should().Contain(Permission.ViewReports);
    }

    [Fact]
    public void CustomRole_HasPermission_ShouldReturnCorrectly()
    {
        var role = new CustomRole();
        role.SetPermissions([Permission.ViewTransactions]);

        role.HasPermission(Permission.ViewTransactions).Should().BeTrue();
        role.HasPermission(Permission.ManageRoles).Should().BeFalse();
    }

    [Fact]
    public void CustomRole_EmptyPermissions_ShouldReturnEmptyList()
    {
        var role = new CustomRole();
        role.GetPermissions().Should().BeEmpty();
    }
}
