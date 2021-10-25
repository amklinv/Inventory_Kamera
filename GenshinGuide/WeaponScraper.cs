﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace GenshinGuide
{
	public static class WeaponScraper
	{
		public static void ScanWeapons(int count = 0)
		{
			// Determine maximum number of weapons to scan
			int weaponCount = count == 0 ? ScanWeaponCount(): count;
			UserInterface.SetWeapon_Max(weaponCount);
			int cardsQueued = 0;

			// Where in screen space weapons are
			int itemX = (int)(Navigation.GetArea().Right * (21 / (Double)160));
			int itemY = (int)(Navigation.GetArea().Bottom * (14 / (Double)90));

			// Inventory has 7 columns with 5 rows default
			int maxColumns = 7;
			int maxRows = 5;
			int column = 0;
			int row = 0;

			// offset used to move mouse to other weapons
			int xOffset = Convert.ToInt32(Navigation.GetArea().Right * (12.25 / 160));
			int yOffset = Convert.ToInt32(Navigation.GetArea().Bottom * (14.5 / 90));

			// Go through weapon list
			while (cardsQueued < weaponCount)
			{
				// Select item
				int nextOffsetX = (xOffset * (cardsQueued % maxColumns));
				int nextOffsetY = (yOffset * (row % maxRows));
				Navigation.SetCursorPos(Navigation.GetPosition().Left + itemX + nextOffsetX, Navigation.GetPosition().Top + itemY + nextOffsetY);
				Navigation.sim.Mouse.LeftButtonClick();
				Navigation.SystemRandomWait(Navigation.Speed.SelectNextInventoryItem);


				// Queue card for scanning
				QueueScan(cardsQueued);
				cardsQueued++;
				column++;


				// Reach end of row
				if (column == maxColumns)
				{
					// reset mouse pointer and scroll down weapon list
					column = 0;
					row++;

					// Scroll to next chunk
					if (cardsQueued % (maxRows * maxColumns) == 0)
					{
						for (int i = 0; i < 50; i++)
						{
							Navigation.sim.Mouse.VerticalScroll(-1);
							Navigation.SystemRandomWait(Navigation.Speed.Instant);
						}
						row = 0;
					}
				}
			}
		}

		public static void QueueScan(int id)
		{

			// Grab image of entire card on Right
			RECT itemCard = new RECT(new Rectangle(862, 80, 325, 560));
			Bitmap bm = Navigation.CaptureRegion(itemCard);

			// Separate to all pieces of card
			List<Bitmap> weaponImages = new List<Bitmap>();

			// Name
			int xOffset = 10;
			int yOffset = 7;
			Bitmap name = bm.Clone(new Rectangle(xOffset, yOffset, itemCard.Width - 2 * xOffset, 25), bm.PixelFormat);

			// Level
			xOffset = 19;
			yOffset = 206;
			Bitmap level = bm.Clone(new Rectangle(xOffset, yOffset, 88, 19), bm.PixelFormat);

			// Refinement
			xOffset = 19;
			yOffset = 234;
			Bitmap refinement = bm.Clone(new Rectangle(xOffset, yOffset, 24, 20), bm.PixelFormat);

			// Equipped Character
			xOffset = 50;
			yOffset = 529;
			Bitmap equipped = bm.Clone(new Rectangle(xOffset, yOffset, itemCard.Width - xOffset, 25), bm.PixelFormat);

			// Assign to List
			weaponImages.Add(name);
			weaponImages.Add(level);
			weaponImages.Add(refinement);
			weaponImages.Add(equipped);

			// Send Image to Worker Queue
			GenshinData.workerQueue.Enqueue(new OCRImage(weaponImages, "weapon", id));
		}

		public static Weapon CatalogueFromBitmaps(List<Bitmap> bm, int id)
		{

			// Init Variables
			int name = 0;
			int level = 1;
			bool ascension = false;
			int refinementLevel = 1;
			int equippedCharacter = 0;

			if (bm.Count == 4)
			{
				int w_name = 0; int w_level = 1; int w_refinement = 2; int w_equippedCharacter = 3;
				// Check for Rarity
				Color rarityColor = bm[0].GetPixel(5, 5);
				Color fiveStar = Color.FromArgb(255, 188, 105, 50);
				Color fourthStar = Color.FromArgb(255, 161, 86, 224);
				Color threeStar = Color.FromArgb(255, 81, 127, 203);
				// Check for equipped color
				Color equipped = Color.FromArgb(255, 255, 231, 187);

				// Scan different parts of the weapon
				bool b_RarityAboveTwo = Scraper.CompareColors(fiveStar, rarityColor) || Scraper.CompareColors(fourthStar, rarityColor) || Scraper.CompareColors(threeStar, rarityColor);

				if (b_RarityAboveTwo)
				{

					Thread thr1 = new Thread(() => name = ScanName(bm[w_name]));
					Thread thr2 = new Thread(() => level = ScanLevel(bm[w_level],ref ascension));
					Thread thr3 = new Thread(() => refinementLevel = ScanRefinement(bm[w_refinement]));
					Thread thr4 = new Thread(() => equippedCharacter = ScanEquippedCharacter(bm[w_equippedCharacter]));

					// Start Threads
					thr1.Start(); thr2.Start(); thr3.Start(); thr4.Start();

					// End Threads
					thr1.Join(); thr2.Join(); thr3.Join(); thr4.Join();

					// dispose the list
					foreach (Bitmap x in bm)
					{
						x.Dispose();
					}
				}
				else
				{
					name = -1; refinementLevel = -1; equippedCharacter = -1;
				}
			}

			return new Weapon(name, level, ascension, refinementLevel, equippedCharacter, id);
		}

		public static bool IsEnhancementOre()
		{
			// Grab image of card on right side of screen
			int width = 325; int height = 560;

			Rectangle card = new Rectangle(865, 83, width, height);
			using (var cardBitMap = Navigation.CaptureRegion(card))
			{
				return ScanEnchancementOreName(cardBitMap) > 0;
			}
		}

		public static bool IsEnhancementOre(Bitmap nameBitmap)
		{
			return ScanEnchancementOreName(nameBitmap) > 0;
		}

		public static int ScanWeaponCount()
		{
			//Find weapon count
			Bitmap bm = Navigation.CaptureRegion(new Rectangle(1060,20,145,25));

			Scraper.SetGrayscale(ref bm);
			Scraper.SetContrast(60.0, ref bm);
			Scraper.SetInvert(ref bm);
			UserInterface.SetNavigation_Image(bm);

			string text = Scraper.AnalyzeText(bm);
			text = Regex.Replace(text, @"[^\d/]", "");

			int count;
			// Check for dash
			if (Regex.IsMatch(text, "/"))
			{
				count = Int32.Parse(text.Split('/')[0]);
			}
			else
			{
				// divide by the number on the right if both numbers fused
				count = Int32.Parse(text) / 2000;
			}

			// Check if larger than 1000
			while (count > 2000)
			{
				count /= 20;
			}


			return count;
		}

		public static int ScanName(Bitmap bm)
		{
			Scraper.SetGamma(0.2, 0.2, 0.2, ref bm);
			Scraper.SetGrayscale(ref bm);
			Scraper.SetInvert(ref bm);

			// Analyze
			string text = Scraper.AnalyzeText_1(bm);
			text = text.Trim();
			text = Regex.Replace(text, @"[\W]", "");
			text = text.ToLower();

			Debug.WriteLine($"Weapon name: {text} scanned");

			UserInterface.SetArtifact_GearSlot(bm, text, true);

			// Check in Dictionary
			int name = Scraper.GetWeaponCode(text);
			return name;
		}

		public static int ScanEnchancementOreName(Bitmap bm)
		{
			Scraper.SetGamma(0.2, 0.2, 0.2, ref bm);
			Scraper.SetGrayscale(ref bm);
			Scraper.SetInvert(ref bm);

			// Analyze
			string text = Scraper.AnalyzeText(bm);
			text = text.Trim();
			bm.Dispose();

			text = text.Trim();
			text = Regex.Replace(text, @"[\W_]", "");
			text = text.ToLower();

			return Scraper.GetEnhancementMaterialCode(text);
		}

		public static int ScanLevel(Bitmap bm, ref bool ascension)
		{
			Scraper.SetGrayscale(ref bm);
			Scraper.SetInvert(ref bm);
			Scraper.SetContrast(100.0, ref bm);

			string text = Scraper.AnalyzeText_2(bm);
			text = Regex.Replace(text, @"(?![\d/]).", "");
			text = text.Trim();

			UserInterface.SetWeapon_Level(bm, text);

			if (text.Contains('/'))
			{
				string[] temp = text.Split(new[] { '/' }, 2);

				if (temp.Length == 2)
				{
					if (temp[0] == temp[1])
						ascension = true;

					if (int.TryParse(temp[0], out int level))
					{
						return level;
					}
					else
					{

					}
				}
			}
			return -1;
		}

		public static int ScanRefinement(Bitmap bm)
		{
			Scraper.SetInvert(ref bm);
			Scraper.SetGrayscale(ref bm);

			string text = Scraper.AnalyzeText_3(bm);
			text = text.Trim();
			text = Regex.Replace(text, @"[^\d]", "");

			// Parse Int
			if (int.TryParse(text, out int refinementLevel))
			{
				UserInterface.SetGear_Level(bm, text, true);
				bm.Dispose();
				return refinementLevel;
			}
			return -1;
		}

		public static int ScanEquippedCharacter(Bitmap bm)
		{
			Scraper.SetGrayscale(ref bm);
			Scraper.SetContrast(60.0, ref bm);

			string extractedString = Scraper.AnalyzeText_4(bm);
			extractedString.Trim();

			if (extractedString != "")
			{
				var regexItem = new Regex("Equipped:");
				if (regexItem.IsMatch(extractedString))
				{
					string[] tempString = extractedString.Split(':');
					extractedString = tempString[1].Replace("\n", String.Empty);
					UserInterface.SetGear_Equipped(bm, extractedString);
					extractedString = Regex.Replace(extractedString, @"[^\w_]", "");
					extractedString = extractedString.ToLower();

					// Assign Traveler Name if not found
					int character = Scraper.GetCharacterCode(extractedString);
					if (Scraper.b_AssignedTravelerName == false && character == 1)
					{
						Scraper.AssignTravelerName(extractedString);
						Scraper.b_AssignedTravelerName = true;
					}

					// Used to match with Traveler Name
					while (extractedString.Length > 1)
					{
						int temp = Scraper.GetCharacterCode(extractedString, true);
						if (temp == -1)
						{
							extractedString = extractedString.Substring(0, extractedString.Length - 1);
						}
						else
						{
							break;
						}
					}
					return extractedString.Length > 0 ? Scraper.GetCharacterCode(extractedString) : 0;
				}
			}
			// artifact has no equipped character
			return 0;
		}
	}
}
