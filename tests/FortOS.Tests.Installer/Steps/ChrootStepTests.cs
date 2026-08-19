using FortOS.Installer.Core.Models;
using FortOS.Installer.Core.Steps;

namespace FortOS.Tests.Installer.Steps;

public class ChrootStepTests
{
    private static InstallContext Context(RootFileSystem rootFs = RootFileSystem.Btrfs, DataDiskConfig? data = null)
    {
        var config = new InstallConfig
        {
            SystemDisk = "/dev/sda",
            RootFs = rootFs,
            Data = data ?? new DataDiskConfig { Mode = DataDiskMode.Single, Disk = "/dev/sdb", FileSystem = DataFileSystem.Btrfs },
            Network = new NetworkConfig { Hostname = "MyNas" },
            Account = new AccountConfig { Username = "admin", Timezone = "Asia/Shanghai" },
        };
        var context = new InstallContext { Config = config, SourcePath = "/", TargetMount = "/target" };
        context.Uuids["root"] = "root-uuid";
        context.Uuids["efi"] = "efi-uuid";
        context.Uuids["swap"] = "swap-uuid";
        context.Uuids["data"] = "data-uuid";
        return context;
    }

    [Fact]
    public void BuildFstab_UsesUuidsAndConfiguredFs()
    {
        var fstab = ChrootStep.BuildFstab(Context());

        Assert.Contains("UUID=root-uuid / btrfs defaults,noatime 0 1", fstab);
        Assert.Contains("UUID=efi-uuid /boot/efi vfat umask=0077 0 1", fstab);
        Assert.Contains("UUID=swap-uuid none swap sw 0 0", fstab);
        Assert.Contains("UUID=data-uuid /srv/nas btrfs defaults,noatime 0 2", fstab);
    }

    [Fact]
    public void BuildFstab_Ext4RootAndNoData()
    {
        var fstab = ChrootStep.BuildFstab(Context(rootFs: RootFileSystem.Ext4));

        Assert.Contains("UUID=root-uuid / ext4 defaults,noatime 0 1", fstab);
    }

    [Fact]
    public void BuildFstab_OmitsOptionalEntriesWhenAbsent()
    {
        var context = Context(data: new DataDiskConfig { Mode = DataDiskMode.None });
        context.Uuids.Remove("efi");
        context.Uuids.Remove("swap");
        context.Uuids.Remove("data");

        var fstab = ChrootStep.BuildFstab(context);

        Assert.Single(fstab.TrimEnd().Split('\n'));
    }

    [Fact]
    public void BuildFstab_LuksData_UsesMapperDevice()
    {
        var context = Context(data: new DataDiskConfig
        {
            Mode = DataDiskMode.Luks,
            Disk = "/dev/sdb",
            FileSystem = DataFileSystem.Btrfs,
            LuksMapperName = "fortos-data",
        });
        context.Uuids["data-luks"] = "luks-container-uuid";

        var fstab = ChrootStep.BuildFstab(context);

        Assert.Contains("/dev/mapper/fortos-data /srv/nas btrfs defaults,noatime 0 2", fstab);
        Assert.DoesNotContain("UUID=data-uuid /srv/nas", fstab);
    }

    [Fact]
    public void BuildCrypttab_LuksData_WritesMapperEntry()
    {
        var context = Context(data: new DataDiskConfig
        {
            Mode = DataDiskMode.Luks,
            Disk = "/dev/sdb",
            LuksMapperName = "fortos-data",
        });
        context.Uuids["data-luks"] = "luks-container-uuid";

        var crypttab = ChrootStep.BuildCrypttab(context);

        Assert.Equal("fortos-data UUID=luks-container-uuid none luks\n", crypttab);
    }

    [Fact]
    public void BuildCrypttab_NoLuks_IsEmpty()
    {
        var context = Context(data: new DataDiskConfig { Mode = DataDiskMode.None });

        Assert.Equal(string.Empty, ChrootStep.BuildCrypttab(context));
    }

    [Theory]
    [InlineData("en_US.UTF-8", "en_US.UTF-8 UTF-8\n")]
    [InlineData("zh_CN.UTF-8", "en_US.UTF-8 UTF-8\nzh_CN.UTF-8 UTF-8\n")]
    [InlineData("ja_JP.UTF-8", "en_US.UTF-8 UTF-8\nja_JP.UTF-8 UTF-8\n")]
    public void BuildLocaleGen_KeepsEnglishFallbackAndAddsSelected(string language, string expected)
        => Assert.Equal(expected, ChrootStep.BuildLocaleGen(language));

    [Theory]
    [InlineData("zh_CN", "zh_CN.UTF-8")]
    [InlineData("zh_CN.UTF-8", "zh_CN.UTF-8")]
    [InlineData("zh_CN.utf-8", "zh_CN.UTF-8")]
    [InlineData("zh_CN.GB2312", "zh_CN.UTF-8")]
    [InlineData("de_DE", "de_DE.UTF-8")]
    [InlineData("en_US.UTF-8", "en_US.UTF-8")]
    [InlineData("", "en_US.UTF-8")]
    [InlineData(".UTF-8", "en_US.UTF-8")]
    [InlineData("sr_RS@latin", "en_US.UTF-8")]
    [InlineData(null, "en_US.UTF-8")]
    public void NormalizeLanguage_ForcesUtf8Suffix(string? input, string expected)
        => Assert.Equal(expected, ChrootStep.NormalizeLanguage(input));

    [Fact]
    public void SeedFortosUserDb_CreatesAdminUser()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"fortos-seed-{Guid.NewGuid():N}.db");
        try
        {
            ChrootStep.SeedFortosUserDb(dbPath, "admin", "MyPassw0rd!");

            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT username, password_hash, roles_json FROM users WHERE username = $u;";
                cmd.Parameters.AddWithValue("$u", "admin");
                using var reader = cmd.ExecuteReader();
                Assert.True(reader.Read(), "user row should exist");
                Assert.Equal("admin", reader.GetString(0));
                Assert.True(BCrypt.Net.BCrypt.Verify("MyPassw0rd!", reader.GetString(1)), "password hash must verify");
                var roles = System.Text.Json.JsonSerializer.Deserialize<string[]>(reader.GetString(2));
                Assert.Contains("admin", roles);
                Assert.Contains("user", roles);
            }
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public void SeedFortosUserDb_IsIdempotent_KeepsOriginalHash()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"fortos-seed-{Guid.NewGuid():N}.db");
        try
        {
            ChrootStep.SeedFortosUserDb(dbPath, "admin", "FirstPassw0rd!");
            ChrootStep.SeedFortosUserDb(dbPath, "admin", "SecondPassw0rd!");

            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT password_hash FROM users WHERE username = $u;";
                cmd.Parameters.AddWithValue("$u", "admin");
                var hash = (string)cmd.ExecuteScalar()!;
                Assert.True(BCrypt.Net.BCrypt.Verify("FirstPassw0rd!", hash), "original password must be preserved");
                Assert.False(BCrypt.Net.BCrypt.Verify("SecondPassw0rd!", hash), "second call must not overwrite");
            }
        }
        finally
        {
            File.Delete(dbPath);
        }
    }
}
