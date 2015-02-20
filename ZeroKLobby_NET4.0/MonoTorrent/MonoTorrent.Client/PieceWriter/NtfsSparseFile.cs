using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MonoTorrent.Client
{
	static class SparseFile
	{
		const uint FILE_SUPPORTS_SPARSE_FILES = 64;
		const uint FSCTL_SET_SPARSE = ((uint)0x00000009 << 16) | ((uint)49 << 2);
		const uint FSCTL_SET_ZERO_DATA = ((uint)0x00000009 << 16) | ((uint)50 << 2) | ((uint)2 << 14);
		const int MAX_PATH = 260;

		static bool SupportsSparse = true;

		public static void CreateSparse(string filename, long length)
		{
			if (!SupportsSparse) return;

			// Ensure we have the full path
			filename = Path.GetFullPath(filename);
			try
			{
				if (!CanCreateSparse(filename)) return;

				// Create a file with the sparse flag enabled

				uint bytesReturned = 0;
				var access = (uint)0x40000000; // GenericWrite
				uint sharing = 0; // none
				var attributes = (uint)0x00000080; // Normal
				var creation = (uint)1; // Only create if new

				using (var handle = CreateFileW(filename, access, sharing, IntPtr.Zero, creation, attributes, IntPtr.Zero))
				{
					// If we couldn't create the file, bail out
					if (handle.IsInvalid) return;

					// If we can't set the sparse bit, bail out
					if (!DeviceIoControl(handle, FSCTL_SET_SPARSE, IntPtr.Zero, 0, IntPtr.Zero, 0, ref bytesReturned, IntPtr.Zero)) return;

					// Tell the filesystem to mark bytes 0 -> length as sparse zeros
					var data = new FILE_ZERO_DATA_INFORMATION(0, length);
					var structSize = (uint)Marshal.SizeOf(data);
					var ptr = Marshal.AllocHGlobal((int)structSize);

					try
					{
						Marshal.StructureToPtr(data, ptr, false);
						DeviceIoControl(handle, FSCTL_SET_ZERO_DATA, ptr, structSize, IntPtr.Zero, 0, ref bytesReturned, IntPtr.Zero);
					}
					finally
					{
						Marshal.FreeHGlobal(ptr);
					}
				}
			}
			catch (DllNotFoundException)
			{
				SupportsSparse = false;
			}
			catch (EntryPointNotFoundException)
			{
				SupportsSparse = false;
			}
			catch
			{
				// Ignore for now. Maybe if i keep hitting this i should abort future attemts
			}
		}

		static bool CanCreateSparse(string volume)
		{
			// Ensure full path is supplied
			volume = Path.GetPathRoot(volume);

			var volumeName = new StringBuilder(MAX_PATH);
			var systemName = new StringBuilder(MAX_PATH);

			uint fsFlags, serialNumber, maxComponent;

			var result = GetVolumeInformationW(volume, volumeName, MAX_PATH, out serialNumber, out maxComponent, out fsFlags, systemName, MAX_PATH);
			return result && (fsFlags & FILE_SUPPORTS_SPARSE_FILES) == FILE_SUPPORTS_SPARSE_FILES;
		}


		[DllImportAttribute("kernel32.dll")]
		static extern SafeFileHandle CreateFileW([In] [MarshalAsAttribute(UnmanagedType.LPWStr)] string lpFileName,
		                                         uint dwDesiredAccess,
		                                         uint dwShareMode,
		                                         [In] IntPtr lpSecurityAttributes,
		                                         uint dwCreationDisposition,
		                                         uint dwFlagsAndAttributes,
		                                         [In] IntPtr hTemplateFile);

		[DllImport("Kernel32.dll")]
		static extern bool DeviceIoControl(SafeFileHandle hDevice,
		                                   uint dwIoControlCode,
		                                   IntPtr InBuffer,
		                                   uint nInBufferSize,
		                                   IntPtr OutBuffer,
		                                   uint nOutBufferSize,
		                                   ref uint pBytesReturned,
		                                   [In] IntPtr lpOverlapped);

		[DllImportAttribute("kernel32.dll")]
		static extern bool GetVolumeInformationW([In] [MarshalAsAttribute(UnmanagedType.LPWStr)] string lpRootPathName,
		                                         [Out] [MarshalAsAttribute(UnmanagedType.LPWStr)] StringBuilder lpVolumeNameBuffer,
		                                         uint nVolumeNameSize,
		                                         out uint lpVolumeSerialNumber,
		                                         out uint lpMaximumComponentLength,
		                                         out uint lpFileSystemFlags,
		                                         [Out] [MarshalAsAttribute(UnmanagedType.LPWStr)] StringBuilder lpFileSystemNameBuffer,
		                                         uint nFileSystemNameSize);

		[StructLayout(LayoutKind.Sequential)]
		struct FILE_ZERO_DATA_INFORMATION
		{
			public FILE_ZERO_DATA_INFORMATION(long offset, long count)
			{
				FileOffset = offset;
				BeyondFinalZero = offset + count;
			}

			public readonly long FileOffset;
			public readonly long BeyondFinalZero;
		}
	}
}