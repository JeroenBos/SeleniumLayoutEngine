﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using JBSnorro;

namespace JBSnorro.Web
{
	class Cache
	{
		private static readonly int layoutEngineVersionHash = Assembly.GetExecutingAssembly().GetName().Version?.GetHashCode() ?? 0;

		private readonly bool headless;
		private readonly string cachePath;
		private readonly int zoom;
		public Cache(string cachePath, bool headless, int zoom = 100)
		{
			this.cachePath = cachePath;
			this.headless = headless;
			this.zoom = zoom;
		}

		/// <summary>
		/// Gets the rectangles if if exists in the cache.
		/// </summary>
		public async Task<(IEnumerable<TaggedRectangle>? Rectangles, string Hash)> TryGetValue(string? file, string? dir)
		{
			if (file is null == dir is null)
				throw new ArgumentException("Either file or dir must be provided");

			var hash = (await ComputeHash(file, dir)).ToString();
			var path = Path.Combine(cachePath, hash);
			if (File.Exists(path))
			{
				var entry = CacheFile.CacheEntry.Parse(File.ReadAllLines(path));
				return (entry.Rectangles, hash);
			}

			return (null, hash);
		}
		public Task Write(string? file, string? dir, string hash, IEnumerable<TaggedRectangle> rectangles)
		{
			if (file is null == dir is null)
				throw new ArgumentException("Either file or dir must be provided");

			Directory.CreateDirectory(cachePath);

			string path = Path.Combine(cachePath, hash);
			try
			{
				return File.WriteAllLinesAsync(path, new CacheFile.CacheEntry() { Hash = hash, Rectangles = rectangles }.Lines);
			}
			catch (IOException e)
			{
				Console.Error.WriteLine("Ignoring error in writing cache");
				Console.Error.WriteLine(e.GetType().Name + ": " + e.Message);
				return Task.CompletedTask;
			}
		}

		internal async Task<nuint> ComputeHash(string? file, string? dir)
		{
			if (dir != null)
				dir = Path.GetFullPath(dir);
			var allRelevantFilenames = file != null ? new string[] { file } : dir!.GetAllFilenamesRecursively();
			var subfilenames = file != null ? new string[] { "" } : allRelevantFilenames.Select(path => Path.GetRelativePath(dir!, path));

			var hashCodeTasks = allRelevantFilenames.Zip(subfilenames, (fullpath, subpath) => Task.Run(() => fullpath.ComputeFileHash() + subpath.ComputeHash()));
			var hashCodes = await Task.WhenAll(hashCodeTasks);

			unchecked
			{
				var sum = hashCodes.Sum();
				var versionHash = (nuint)layoutEngineVersionHash;
				var headlessHash = this.headless ? 0 : (nuint)1;
				var zoomHash = (nuint)this.zoom;
				return sum + versionHash + headlessHash + zoomHash;
			}
		}
	}

	class CacheFile
	{
		internal const string newEntryPrefix = "#";
		internal readonly Dictionary<string, CacheEntry> entries;
		public static async Task<CacheFile> ReadFrom(string path)
		{
			var lines = File.Exists(path) ? (await File.ReadAllLinesAsync(path)).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray() : Array.Empty<string>();
			return new CacheFile(Parse(lines).Select(entry => KeyValuePair.Create(entry.Hash, entry)));
		}

		public CacheFile(IEnumerable<KeyValuePair<string, CacheEntry>> entries)
		{
			this.entries = new Dictionary<string, CacheEntry>(entries);
		}

		internal static IEnumerable<CacheEntry> Parse(string[] lines)
		{
			for (int index = 0; index < lines.Length; index++)
			{
				string line = lines[index];
				if (line.StartsWith(newEntryPrefix))
				{
					int endIndex = index + 1 + lines.Skip(index + 1).IndexOf(l => l.StartsWith(newEntryPrefix));
					if (endIndex == index) // i.e. IndexOf returned -1
						endIndex = lines.Length;
					yield return new CacheEntry
					{
						Hash = line[newEntryPrefix.Length..],
						Output = lines[(index + 1)..endIndex]
					};
				}
			}
		}

		public record CacheEntry
		{
			public string Hash { get; internal init; } = default!;
			public string[] Output { get; internal init; } = default!;

			internal static CacheEntry Parse(string[] lines)
			{
				if (lines.Length == 0) throw new ArgumentException("File is empty");
				if (lines.Length == 1) throw new ArgumentException("File has only one line: '" + lines[0] + "'");
				if (!lines[0].StartsWith(newEntryPrefix)) throw new ArgumentException("lines[0].StartsWith(newEntryPrefix)");

				return new CacheEntry
				{
					Hash = lines[0][newEntryPrefix.Length..],
					Output = lines[1..]
				};
			}


			public IEnumerable<TaggedRectangle> Rectangles
			{
				get
				{
					return Output.Select(line =>
					{
						var parts = line.Split(",");
						if (parts.Length != 5)
							throw new Exception("Expected a tag and 4 values for rectangle");
						var tag = parts[0];
						var values = parts[1..].Select(float.Parse).ToArray();
						return new TaggedRectangle(tag, values[0], values[1], values[2], values[3]);
					});
				}
				init
				{
					if (this.Output != null)
						throw new ArgumentException("Rectangles can only be set when Output isn't");

					this.Output = value.Select(r => r.Format()).ToArray();
				}
			}
			public IEnumerable<string> Lines
			{
				get
				{
					yield return newEntryPrefix + Hash;
					foreach (var outputLine in Output)
						yield return outputLine;
				}
			}
		}
	}
}
