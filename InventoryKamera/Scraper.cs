﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using Accord;
using Accord.Imaging.Filters;
using Newtonsoft.Json.Linq;
using Tesseract;

namespace InventoryKamera
{
	public static class Scraper
	{
		private const int numEngines = 8;
#if DEBUG
		public static bool s_bDoDebugOnlyCode = false;

		
#endif
		private static readonly string tesseractDatapath = $"{Directory.GetCurrentDirectory()}\\tessdata";
		private static readonly string tesseractLanguage = "genshin_fast_09_04_21";

		// GLOBALS
		public static bool b_AssignedTravelerName = false;

		public static readonly Dictionary<string, string> Stats = new Dictionary<string, string>
		{
			["hp"] = "hp",
			["hp%"] = "hp_",
			["atk"] = "atk",
			["atk%"] = "atk_",
			["def"] = "def",
			["def%"] = "def_",
			["energyrecharge"] = "enerRech_",
			["elementalmastery"] = "eleMas",
			["healingbonus"] = "heal_",
			["critrate"] = "critRate_",
			["critdmg"] = "critDMG_",
			["physicaldmgbonus"] = "physical_dmg_",
			["anemodmgbonus"] = "anemo_dmg_",
			["pyrodmgbonus"] = "pyro_dmg_",
			["electrodmgbonus"] = "electro_dmg_",
			["cryodmgbonus"] = "cryo_dmg_",
			["hydrodmgbonus"] = "hydro_dmg_",
			["geodmgbonus"] = "geo_dmg_"
		};

		public static readonly List<string> gearSlots = new List<string>
		{
			"flower",
			"plume",
			"sands",
			"goblet",
			"circlet",
		};

		public static readonly List<string> elements = new List<string>
		{
			"pyro",
			"hydro",
			"dendro",
			"electro",
			"anemo",
			"cryo",
			"geo",
		};


		public static readonly HashSet<string> enhancementMaterials = new HashSet<string>
		{
			"enhancementore",
			"fineenhancementore",
			"mysticenhancementore",
			"sanctifyingunction",
			"sanctifyingessence",
		};

		public static ConcurrentBag<TesseractEngine> engines = new ConcurrentBag<TesseractEngine>();


		public static readonly Dictionary<string, string> Artifacts, Weapons, DevMaterials, Materials, AllMaterials, Elements;

		public static Dictionary<string, JObject> Characters;

		static Scraper()
		{
			for (int i = 0; i < numEngines; i++)
			{
				engines.Add(new TesseractEngine(tesseractDatapath, tesseractLanguage, EngineMode.LstmOnly));
			}

			var listManager = new DatabaseManager();

			Characters = listManager.LoadCharacters();
			Artifacts = listManager.LoadArtifacts();
			Weapons = listManager.LoadWeapons();
			DevMaterials = listManager.LoadDevMaterials();
			Materials = listManager.LoadMaterials();
			AllMaterials = listManager.LoadAllMaterials();
			Elements = new Dictionary<string, string>();

			foreach (var element in elements)	Elements.Add(element, char.ToUpper(element[0]) + element.Substring(1));
		}

		public static void AddTravelerToCharacterList(string traveler)
		{
			Characters = new DatabaseManager().LoadCharacters();

			if (!Characters.ContainsKey(traveler))
			{
				if (Characters.TryGetValue("traveler", out JObject value))
				{
					Characters.Add(traveler, value);
					Characters.Remove("traveler");
				}
				else throw new KeyNotFoundException("Could not find 'traveler' entry in characters.json");
			}

		}

		public static void AssignTravelerName(string traveler)
		{
			if (!string.IsNullOrEmpty(traveler))
			{
				AddTravelerToCharacterList(traveler);
				Debug.WriteLine($"Parsed traveler name {traveler}");
			}
			else
			{
				UserInterface.AddError("Could not parse Traveler's username");
			}
		}

		#region OCR

		public static void RestartEngines()
		{
			lock (engines)
			{
				while (!engines.IsEmpty)
				{
					if (engines.TryTake(out TesseractEngine e))
						e.Dispose();
				}

				for (int i = 0; i < numEngines; i++)
				{
					engines.Add(new TesseractEngine(tesseractDatapath, tesseractLanguage, EngineMode.LstmOnly));
				}
			}
		}

		/// <summary> Use Tesseract OCR to find words on picture to string </summary>
		public static string AnalyzeText(Bitmap bitmap, PageSegMode pageMode = PageSegMode.SingleLine, bool numbersOnly = false)
		{
			string text = "";
			TesseractEngine e;
			while (!engines.TryTake(out e)) { Thread.Sleep(10); }

			if (numbersOnly) e.SetVariable("tessedit_char_whitelist", "0123456789");
			using (var page = e.Process(bitmap, pageMode))
			{
				using (var iter = page.GetIterator())
				{
					iter.Begin();
					do
					{
						text += iter.GetText(PageIteratorLevel.TextLine);
					}
					while (iter.Next(PageIteratorLevel.TextLine));
				}
			}
			engines.Add(e);

			return text;
		}

		public static string AnalyzeFewText(Bitmap img)
		{
			string text = "";
			using (var ocr = new TesseractEngine(tesseractDatapath, "eng", EngineMode.TesseractOnly))
			{
				var page = ocr.Process(img, PageSegMode.SparseText);
				ocr.SetVariable("tessedit_char_whitelist", "0123456789");
				text = page.GetText();
			}
			return text;
		}

		#endregion OCR

		#region Check valid parameters

		public static bool IsValidSetName(string setName)
		{
			if (Artifacts.Keys.Contains(setName) || Artifacts.Values.Contains(setName))
			{
				return true;
			}
			else
			{
				Debug.WriteLine($"Error: {setName} is not a valid set name");
				UserInterface.AddError($"{setName} is not a valid set name");
				return false;
			};
		}

		internal static bool IsValidMaterial(string name)
		{
			if (AllMaterials.Keys.Contains(name) || AllMaterials.Values.Contains(name))
			{
				return true;
			}
			else
			{
				Debug.WriteLine($"Error: {name} is not a valid material");
				UserInterface.AddError($"{name} is not a valid material");
				return false;
			};
		}

		public static bool IsValidStat(string stat)
		{
			if (string.IsNullOrWhiteSpace(stat) || Stats.Keys.Contains(stat) || Stats.Values.Contains(stat) )
			{
				return true;
			}
			else
			{
				Debug.WriteLine($"Error: {stat} is not a valid stat name");
				UserInterface.AddError($"{stat} is not a valid stat name");
				return false;
			};
		}

		public static bool IsValidSlot(string gearSlot)
		{
			if (gearSlots.Contains(gearSlot))
			{
				return true;
			}
			else
			{
				Debug.WriteLine($"Error: {gearSlot} is not a valid gear slot");
				UserInterface.AddError($"{gearSlot} is not a valid gear slot");
				return false;
			};
		}

		public static bool IsValidCharacter(string character)
		{
			if (character == "Traveler" || Characters.Keys.Contains(character.ToLower()))
			{
				return true;
			}
			else
			{
				Debug.WriteLine($"{character} is not a valid character name");
				UserInterface.AddError($"{character} is not a valid character name");
				return false;
			}
		}

		public static bool IsValidElement(string element)
		{
			if (Elements.Keys.Contains(element) || Elements.Values.Contains(element))
			{
				return true;
			}
			else
			{
				Debug.Print($"Error: {element} is not a valid elemental type");
				UserInterface.AddError($"{element} is not a valid elemental type");
				return false;
			};
		}

		public static bool IsEnhancementMaterial(string material)
		{
			return AllMaterials.Keys.Contains(material.ToLower());
		}

		public static bool IsValidWeapon(string weapon)
		{
			if (Weapons.Keys.Contains(weapon) || Weapons.Values.Contains(weapon))
			{
				return true;
			}
			else
			{
				Debug.Print($"Error: {weapon} is not a valid weapon name");
				UserInterface.AddError($"{weapon} is not a valid weapon name");
				return false;
			};
		}

		#endregion Check valid parameters

		#region Element Searching

		public static string FindClosestGearSlot(string gearSlot)
		{
			foreach (var slot in gearSlots)
			{
				if (gearSlot.Contains(slot))
				{
					return slot;
				}
			}
			return null;
		}

		public static string FindClosestStat(string stat)
		{
			return FindClosestInDict(stat, Stats);
		}

		public static string FindElementByName(string name)
		{
			return FindClosestInDict(name, Elements);
		}

		public static string FindClosestWeapon(string name)
		{
			return FindClosestInDict(name, Weapons);
		}

		public static string FindClosestSetName(string name)
		{
			return FindClosestInDict(name, Artifacts);
		}

		public static string FindClosestCharacterName(string name)
		{
			return FindClosestInDict(name, Characters);
		}

		public static string FindClosestDevelopmentName(string name)
		{
			string value = FindClosestInDict(name, DevMaterials);
			return !string.IsNullOrWhiteSpace(value) ? value : FindClosestInDict(name, AllMaterials);
		}

		public static string FindClosestMaterialName(string name)
		{
			string value = FindClosestInDict(name, Materials);
			return !string.IsNullOrWhiteSpace(value) ? value : FindClosestInDict(name, AllMaterials);
		}

		private static string FindClosestInDict(string source, Dictionary<string, string> targets)
		{
			if (string.IsNullOrWhiteSpace(source)) return "";
			if (targets.TryGetValue(source, out string value)) return value;

			HashSet<string> keys = new HashSet<string>(targets.Keys);

			if (source.Length > 5 && keys.Where(key => key.Contains(source)).Count() == 1) return targets[keys.First(key => key.Contains(source))];

			source = FindClosestInList(source, keys);

			return targets.TryGetValue(source, out value) ? value : source;
		}

		private static string FindClosestInDict(string source, Dictionary<string, JObject> targets)
		{
			if (string.IsNullOrWhiteSpace(source)) return "";
			if (targets.TryGetValue(source, out JObject value)) return (string)value["GOOD"];

			HashSet<string> keys = new HashSet<string>(targets.Keys);

			if (keys.Where(key => key.Contains(source)).Count() == 1) return (string)targets[keys.First(key => key.Contains(source))]["GOOD"];

			source = FindClosestInList(source, keys);

			return targets.TryGetValue(source, out value) ? (string)value["GOOD"] : source;
		}

		private static string FindClosestInList(string source, HashSet<string> targets)
		{
			if (targets.Contains(source)) return source;
			if (string.IsNullOrWhiteSpace(source)) return null;

			string value = "";
			int maxEdits = 15;

			foreach (var target in targets)
			{
				int edits = CalcDistance(source, target, maxEdits);

				if (edits < maxEdits)
				{
					value = target;
					maxEdits = edits;
				}
			}
			return value;
		}

		// Adapted from https://stackoverflow.com/a/9454016/13205651
		private static int CalcDistance(string text, string setName, int maxEdits)
		{
			int length1 = text.Length;
			int length2 = setName.Length;

			// Return trivial case - difference in string lengths exceeds threshhold
			if (Math.Abs(length1 - length2) > maxEdits) { return int.MaxValue; }

			// Ensure arrays [i] / length1 use shorter length
			if (length1 > length2)
			{
				Swap(ref setName, ref text);
				Swap(ref length1, ref length2);
			}

			int maxi = length1;
			int maxj = length2;

			int[] dCurrent = new int[maxi + 1];
			int[] dMinus1 = new int[maxi + 1];
			int[] dMinus2 = new int[maxi + 1];
			int[] dSwap;

			for (int i = 0; i <= maxi; i++) { dCurrent[i] = i; }

			int jm1 = 0, im1 = 0, im2 = -1;

			for (int j = 1; j <= maxj; j++)
			{
				// Rotate
				dSwap = dMinus2;
				dMinus2 = dMinus1;
				dMinus1 = dCurrent;
				dCurrent = dSwap;

				// Initialize
				int minDistance = int.MaxValue;
				dCurrent[0] = j;
				im1 = 0;
				im2 = -1;

				for (int i = 1; i <= maxi; i++)
				{
					int cost = text[im1] == setName[jm1] ? 0 : 1;

					int del = dCurrent[im1] + 1;
					int ins = dMinus1[i] + 1;
					int sub = dMinus1[im1] + cost;

					//Fastest execution for min value of 3 integers
					int min = (del > ins) ? (ins > sub ? sub : ins) : (del > sub ? sub : del);

					if (i > 1 && j > 1 && text[im2] == setName[jm1] && text[im1] == setName[j - 2])
						min = Math.Min(min, dMinus2[im2] + cost);

					dCurrent[i] = min;
					if (min < minDistance) { minDistance = min; }
					im1++;
					im2++;
				}
				jm1++;
				if (minDistance > maxEdits) { return int.MaxValue; }
			}

			int result = dCurrent[maxi];
			return ( result > maxEdits ) ? int.MaxValue : result;

			void Swap<T>(ref T arg1, ref T arg2)
			{
				T temp = arg1;
				arg1 = arg2;
				arg2 = temp;
			}
		}

		#endregion Element Searching

		public static bool CompareColors(Color a, Color b)
		{
			int[] diff = new int[3];
			diff[0] = Math.Abs(a.R - b.R);
			diff[1] = Math.Abs(a.G - b.G);
			diff[2] = Math.Abs(a.B - b.B);

			return diff[0] < 10 && diff[1] < 10 && diff[2] < 10;
		}

		#region Image Operations

		public static Bitmap ResizeImage(Image image, int width, int height)
		{
			var destRect = new Rectangle(0, 0, width, height);
			var destImage = new Bitmap(width, height);

			destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);

			using (var graphics = Graphics.FromImage(destImage))
			{
				graphics.CompositingMode = CompositingMode.SourceCopy;
				graphics.CompositingQuality = CompositingQuality.HighQuality;
				graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
				graphics.SmoothingMode = SmoothingMode.HighQuality;
				graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

				using (var wrapMode = new ImageAttributes())
				{
					wrapMode.SetWrapMode(WrapMode.TileFlipXY);
					graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
				}
			}

			return destImage;
		}

		public static Bitmap ConvertToGrayscale(Bitmap bitmap)
		{
			return new Grayscale(0.2125, 0.7154, 0.0721).Apply(bitmap);
		}

		public static void SetContrast(double contrast, ref Bitmap bitmap)
		{
			new ContrastCorrection((int)contrast).ApplyInPlace(bitmap);
		}

		public static void SetGamma(double red, double green, double blue, ref Bitmap bitmap)
		{
			Bitmap temp = bitmap;
			Bitmap bmap = (Bitmap)temp.Clone();
			Color c;
			byte[] redGamma = CreateGammaArray(red);
			byte[] greenGamma = CreateGammaArray(green);
			byte[] blueGamma = CreateGammaArray(blue);
			for (int i = 0; i < bmap.Width; i++)
			{
				for (int j = 0; j < bmap.Height; j++)
				{
					c = bmap.GetPixel(i, j);
					bmap.SetPixel(i, j, Color.FromArgb(redGamma[c.R],
					   greenGamma[c.G], blueGamma[c.B]));
				}
			}
			bitmap = (Bitmap)bmap.Clone();
		}

		private static byte[] CreateGammaArray(double color)
		{
			byte[] gammaArray = new byte[256];
			for (int i = 0; i < 256; ++i)
			{
				gammaArray[i] = (byte)Math.Min(255,
		(int)( ( 255.0 * Math.Pow(i / 255.0, 1.0 / color) ) + 0.5 ));
			}
			return gammaArray;
		}

		public static void SetInvert(ref Bitmap bitmap)
		{
			new Invert().ApplyInPlace(bitmap);
		}

		public static void SetColor
			(string colorFilterType, ref Bitmap bitmap)
		{
			Bitmap temp = bitmap;
			Bitmap bmap = (Bitmap)temp.Clone();
			Color c;
			for (int i = 0; i < bmap.Width; i++)
			{
				for (int j = 0; j < bmap.Height; j++)
				{
					c = bmap.GetPixel(i, j);
					int nPixelR = 0;
					int nPixelG = 0;
					int nPixelB = 0;
					if (colorFilterType == "red")
					{
						nPixelR = c.R;
						nPixelG = c.G - 255;
						nPixelB = c.B - 255;
					}
					else if (colorFilterType == "green")
					{
						nPixelR = c.R - 255;
						nPixelG = c.G;
						nPixelB = c.B - 255;
					}
					else if (colorFilterType == "blue")
					{
						nPixelR = c.R - 255;
						nPixelG = c.G - 255;
						nPixelB = c.B;
					}
					nPixelR = Math.Max(nPixelR, 0);
					nPixelR = Math.Min(255, nPixelR);

					nPixelG = Math.Max(nPixelG, 0);
					nPixelG = Math.Min(255, nPixelG);

					nPixelB = Math.Max(nPixelB, 0);
					nPixelB = Math.Min(255, nPixelB);

					bmap.SetPixel(i, j, Color.FromArgb((byte)nPixelR,
					  (byte)nPixelG, (byte)nPixelB));
				}
			}
			bitmap = (Bitmap)bmap.Clone();
		}

		public static void SetBrightness(int brightness, ref Bitmap bitmap)
		{
			Bitmap temp = bitmap;
			Bitmap bmap = (Bitmap)temp.Clone();
			if (brightness < -255) brightness = -255;
			if (brightness > 255) brightness = 255;
			Color c;
			for (int i = 0; i < bmap.Width; i++)
			{
				for (int j = 0; j < bmap.Height; j++)
				{
					c = bmap.GetPixel(i, j);
					int cR = c.R + brightness;
					int cG = c.G + brightness;
					int cB = c.B + brightness;

					if (cR < 0) cR = 1;
					if (cR > 255) cR = 255;

					if (cG < 0) cG = 1;
					if (cG > 255) cG = 255;

					if (cB < 0) cB = 1;
					if (cB > 255) cB = 255;

					bmap.SetPixel(i, j,
		Color.FromArgb((byte)cR, (byte)cG, (byte)cB));
				}
			}
			bitmap = (Bitmap)bmap.Clone();
		}

		public static void SetThreshold(int threshold, ref Bitmap bitmap)
		{
			new Threshold(threshold).ApplyInPlace(bitmap);
		}

		public static void FilterColors(ref Bitmap bm, IntRange red, IntRange green, IntRange blue)
		{
			ColorFiltering colorFilter = new ColorFiltering
			{
				Red = red,
				Green = green,
				Blue = blue
			};
			colorFilter.ApplyInPlace(bm);
		}

		#endregion Image Operations
	}
}
