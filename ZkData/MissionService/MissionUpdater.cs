//because SharpCompress fails here for some reason

using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO.Compression;
using MonoTorrent.Common;
using ZkData.UnitSyncLib;

namespace ZkData
{
    public class MissionUpdater
    {
        static readonly string MissionFileUrl = GlobalConst.BaseSiteUrl + "/Missions/File/{0}";

        string GetModInfo(string missionNameWithVersion, string modName, string nameWithoutVersion, string shortName)
        {
            const bool hideFromModList = true;
            var sb = new StringBuilder();
            sb.AppendLine("local modinfo = {");
            sb.AppendFormat("  name          =	[[{0}]],\n", missionNameWithVersion);
            sb.AppendFormat("  description   =	[[{0}]],\n", "Mission Mutator"); // the real description might break archivecache.lua
            sb.AppendFormat("  modtype       =	[[{0}]],\n", hideFromModList ? 0 : 1);
            sb.AppendFormat("  shortname     =	[[{0}]],\n", shortName);
            /*sb.AppendFormat("  shortgame     =	[[{0}]],\n", mod.ShortGame);*/
            //sb.AppendFormat("  shortbasename =	[[{0}]],\n", mod.ShortBaseName);
            sb.AppendLine("  depend = {");
            sb.AppendFormat("    [[{0}]]\n", modName);
            sb.AppendLine("  },");
            sb.AppendLine("}");
            sb.AppendLine("return modinfo");
            return sb.ToString();
        }

        static void WriteZipArchiveEntry(ZipArchiveEntry entry, byte[] toWrite)
        {
            using (StreamWriter writer = new StreamWriter(entry.Open()))
            {
                writer.Write(toWrite);
            }
        }
  
        public void UpdateMission(ZkDataContext db, Mission mission, Mod modInfo) {
            var file = mission.Mutator.ToArray();
            var tempName = Path.GetTempFileName() + ".zip";
            File.WriteAllBytes(tempName, file);

            using (var zf = ZipFile.Open(tempName, ZipArchiveMode.Update))
            {
                var modinfoEntry = zf.CreateEntry("modinfo.lua");
                WriteZipArchiveEntry(modinfoEntry, Encoding.UTF8.GetBytes(GetModInfo(mission.NameWithVersion, mission.Mod, mission.Name, "ZK")));
                FixScript(mission, zf, "script.txt");
                var script = FixScript(mission, zf, GlobalConst.MissionScriptFileName);
                modInfo.MissionScript = script;
                //modInfo.ShortName = mission.Name;
                modInfo.Name = mission.NameWithVersion;
            }
            mission.Mutator = File.ReadAllBytes(tempName);
            mission.Script = Regex.Replace(mission.Script, "GameType=([^;]+);", (m) => { return string.Format("GameType={0};", mission.NameWithVersion); });
            
            File.Delete(tempName);
            
            var resource = db.Resources.FirstOrDefault(x => x.MissionID == mission.MissionID); 
            if (resource == null)
            {
                resource = new Resource() { DownloadCount = 0, TypeID = ZkData.ResourceType.Mod };
                db.Resources.Add(resource);
            }
            resource.InternalName = mission.NameWithVersion;
            resource.MissionID = mission.MissionID;

            resource.ResourceDependencies.Clear();
            resource.ResourceDependencies.Add(new ResourceDependency() { NeedsInternalName = mission.Map });
            resource.ResourceDependencies.Add(new ResourceDependency() { NeedsInternalName = mission.Mod });
            resource.ResourceContentFiles.Clear();
            

            // generate torrent
            var tempFile = Path.Combine(Path.GetTempPath(), mission.SanitizedFileName);
            File.WriteAllBytes(tempFile, mission.Mutator.ToArray());
            var creator = new TorrentCreator();
            creator.Path = tempFile;
            var torrentStream = new MemoryStream();
            creator.Create(torrentStream);
            try
            {
                File.Delete(tempFile);
            }
            catch { }

            var md5 = Hash.HashBytes(mission.Mutator.ToArray()).ToString();
            resource.ResourceContentFiles.Add(new ResourceContentFile()
            {
                FileName = mission.SanitizedFileName,
                Length = mission.Mutator.Length,
                LinkCount = 1,
                Links = string.Format(MissionFileUrl, mission.MissionID),
                Md5 = md5
            });


            var basePath = GlobalConst.SiteDiskPath + @"\resources\";
            if (!Directory.Exists(basePath)) Directory.CreateDirectory(basePath);
            File.WriteAllBytes(string.Format(@"{2}\{0}_{1}.torrent", resource.InternalName.EscapePath(), md5, basePath), torrentStream.ToArray());
            
            File.WriteAllBytes(string.Format(@"{1}\{0}.metadata.xml.gz", resource.InternalName.EscapePath(), basePath),
                                   MetaDataCache.SerializeAndCompressMetaData(modInfo));
            
            var imgPath = GlobalConst.SiteDiskPath + @"\img\missions\";
            if (!Directory.Exists(imgPath)) Directory.CreateDirectory(imgPath);
            
            File.WriteAllBytes(string.Format(imgPath + "{0}.png", mission.MissionID, basePath), mission.Image.ToArray());
        }

        static string FixScript(Mission mission, ZipArchive archive, string scriptName)
        {
            ZipArchiveEntry entry = archive.GetEntry(scriptName);
            StreamReader reader = new StreamReader(entry.Open());
            var script = Encoding.UTF8.GetString(Encoding.Default.GetBytes(reader.ReadToEnd()));
            script = Regex.Replace(script, "GameType=([^;]+);", (m) => { return string.Format("GameType={0};", mission.NameWithVersion); });
            WriteZipArchiveEntry(entry, Encoding.UTF8.GetBytes(script));
            return script;
        }
    }


    
}
