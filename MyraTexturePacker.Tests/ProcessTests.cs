using StbImageSharp;
using StbImageWriteSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace MyraTexturePacker.Tests;

public class ProcessTests
{
	private readonly string _testOutputDir;
	private readonly string _inputDir;

	public ProcessTests()
	{
		_inputDir = Path.Combine(AppContext.BaseDirectory, "input");
		_testOutputDir = Path.Combine(AppContext.BaseDirectory, "output");

		CleanOutputDirectory();
	}

	private void CleanOutputDirectory()
	{
		if (!Directory.Exists(_testOutputDir))
		{
			Directory.CreateDirectory(_testOutputDir);
			return;
		}

		try
		{
			// Try to delete all files in the directory
			foreach (var file in Directory.GetFiles(_testOutputDir))
			{
				File.Delete(file);
			}
		}
		catch
		{
			// If cleanup fails, ignore - the test will use the directory as-is
		}
	}

	[Theory]
	[InlineData(128)]
	[InlineData(256)]
	[InlineData(512)]
	[InlineData(1024)]
	public void Process_GeneratesValidAtlas(int atlasSize)
	{
		// Arrange
		var outputFile = Path.Combine(_testOutputDir, $"atlas_{atlasSize}.png");
		var expectedXmlFile = Path.ChangeExtension(outputFile, "xmat");

		// Act
		Program.Process(_inputDir, outputFile, atlasSize, atlasSize);

		// Assert
		// Check output files exist
		Assert.True(File.Exists(outputFile), "Output PNG file was not created");
		Assert.True(File.Exists(expectedXmlFile), "Output XML file was not created");

		// Verify PNG file is valid and readable
		using var pngStream = File.OpenRead(outputFile);
		var atlasImage = ImageResult.FromStream(pngStream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
		Assert.NotNull(atlasImage);

		// Verify atlas size is either the requested size or a power-of-2 multiple if it was too small
		Assert.True(atlasImage.Width >= atlasSize, $"Atlas width {atlasImage.Width} is less than requested {atlasSize}");
		Assert.True(atlasImage.Height >= atlasSize, $"Atlas height {atlasImage.Height} is less than requested {atlasSize}");

		// Verify atlas dimensions are powers of 2 (doubling behavior)
		Assert.True(IsPowerOf2(atlasImage.Width), $"Atlas width {atlasImage.Width} is not a power of 2");
		Assert.True(IsPowerOf2(atlasImage.Height), $"Atlas height {atlasImage.Height} is not a power of 2");

		// Verify atlas width and height are equal
		Assert.Equal(atlasImage.Width, atlasImage.Height);

		// Verify pixel data exists
		Assert.True(atlasImage.Data.Length > 0, "Atlas image has no pixel data");

		// Verify XML structure
		var xmlDoc = XDocument.Load(expectedXmlFile);
		Assert.NotNull(xmlDoc.Root);
		Assert.Equal("TextureAtlas", xmlDoc.Root.Name.LocalName);

		// Verify XML points to the correct image
		var imageAttr = xmlDoc.Root.Attribute("Image");
		Assert.NotNull(imageAttr);
		Assert.Equal($"atlas_{atlasSize}.png", imageAttr.Value);

		// Verify all images from input are referenced in XML
		var inputFiles = Directory.GetFiles(_inputDir, "*.*", SearchOption.TopDirectoryOnly);
		var textureRegions = xmlDoc.Root.Elements("TextureRegion");
		var ninePatchRegions = xmlDoc.Root.Elements("NinePatchRegion");
		var totalRegions = textureRegions.Count() + ninePatchRegions.Count();

		Assert.True(totalRegions > 0, "No texture regions found in XML");
		Assert.True(totalRegions >= 10, $"Expected at least 10 regions, got {totalRegions}");
		Assert.Equal(inputFiles.Length, totalRegions);

		// Verify texture regions have required attributes and are named after input files
		var regionIds = new HashSet<string>();
		foreach (var region in textureRegions)
		{
			AssertRegionAttributes(region);
			regionIds.Add(region.Attribute("Id").Value);
		}

		// Verify nine-patch regions have required attributes and are named after input files
		foreach (var region in ninePatchRegions)
		{
			AssertRegionAttributes(region);
			AssertNinePatchAttributes(region);
			regionIds.Add(region.Attribute("Id").Value);
		}

		// Verify that regions correspond to input files
		foreach (var inputFile in inputFiles)
		{
			var fileName = Path.GetFileNameWithoutExtension(inputFile);
			// Remove .9 suffix for nine-patch files
			if (fileName.EndsWith(".9"))
			{
				fileName = fileName.Substring(0, fileName.Length - 2);
			}
			Assert.True(regionIds.Contains(fileName), $"Input file {inputFile} not found as region in atlas");
		}

		// Verify texture regions have valid coordinates
		foreach (var region in textureRegions.Concat(ninePatchRegions))
		{
			var left = int.Parse(region.Attribute("Left").Value);
			var top = int.Parse(region.Attribute("Top").Value);
			var width = int.Parse(region.Attribute("Width").Value);
			var height = int.Parse(region.Attribute("Height").Value);

			// Verify coordinates are within atlas bounds
			Assert.True(left >= 0, $"Region {region.Attribute("Id").Value} has negative Left coordinate");
			Assert.True(top >= 0, $"Region {region.Attribute("Id").Value} has negative Top coordinate");
			Assert.True(width > 0, $"Region {region.Attribute("Id").Value} has non-positive width");
			Assert.True(height > 0, $"Region {region.Attribute("Id").Value} has non-positive height");
			Assert.True(left + width <= atlasImage.Width,
				$"Region {region.Attribute("Id").Value} extends beyond atlas width");
			Assert.True(top + height <= atlasImage.Height,
				$"Region {region.Attribute("Id").Value} extends beyond atlas height");
		}
	}

	[Fact]
	public void Process_WritesFilterAttributeForFilteredImages()
	{
		// Arrange
		var inputDir = Path.Combine(_testOutputDir, "filter_input");
		Directory.CreateDirectory(inputDir);

		CreatePng(Path.Combine(inputDir, "slider.linear.png"), 16, 16);
		CreatePng(Path.Combine(inputDir, "plain.png"), 16, 16);
		CreateNinePatch(Path.Combine(inputDir, "window.nearest.9.png"), 8, 8);
		CreateNinePatch(Path.Combine(inputDir, "button.anisotropic.9.png"), 8, 8);
		CreateNinePatch(Path.Combine(inputDir, "border.9.png"), 8, 8);

		var outputFile = Path.Combine(_testOutputDir, "filter_atlas.png");
		var xmlFile = Path.ChangeExtension(outputFile, "xmat");

		// Act
		Program.Process(inputDir, outputFile, 256, 256);

		// Assert
		var xmlDoc = XDocument.Load(xmlFile);

		AssertRegionFilter(xmlDoc, "slider", "Linear");
		AssertRegionFilter(xmlDoc, "window", "Nearest");
		AssertRegionFilter(xmlDoc, "button", "Anisotropic");

		// Plain and nine-patch images without explicit filtering must not have the Filter attribute
		var plain = xmlDoc.Root.Elements().First(e => e.Attribute("Id").Value == "plain");
		Assert.Null(plain.Attribute("Filter"));

		var border = xmlDoc.Root.Elements().First(e => e.Attribute("Id").Value == "border");
		Assert.Null(border.Attribute("Filter"));

		// Verify the filter suffix is not a part of the region id
		Assert.DoesNotContain(xmlDoc.Root.Elements(),
			e => e.Attribute("Id").Value.Contains(".linear") || e.Attribute("Id").Value.Contains(".nearest") ||
			e.Attribute("Id").Value.Contains(".anisotropic"));
	}

	private void AssertRegionFilter(XDocument xmlDoc, string id, string expectedFilter)
	{
		var region = xmlDoc.Root.Elements().First(e => e.Attribute("Id").Value == id);
		var filter = region.Attribute("Filter");
		Assert.NotNull(filter);
		Assert.Equal(expectedFilter, filter.Value);
	}

	private void CreatePng(string path, int width, int height)
	{
		using var stream = File.Create(path);
		var imageWriter = new ImageWriter();
		imageWriter.WritePng(CreatePixelData(width, height, new byte[] { 255, 0, 0, 255 }), width, height,
			StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
	}

	private void CreateNinePatch(string path, int width, int height)
	{
		var data = CreatePixelData(width, height, new byte[] { 0, 128, 255, 255 });

		// Draw contiguous black stretchable lines on the top row (columns 1..3)
		for (var x = 1; x <= 3; ++x)
		{
			SetPixel(data, width, x, 0, new byte[] { 0, 0, 0, 255 });
		}

		// Draw contiguous black stretchable lines on the left column (rows 1..3)
		for (var y = 1; y <= 3; ++y)
		{
			SetPixel(data, width, 0, y, new byte[] { 0, 0, 0, 255 });
		}

		using var stream = File.Create(path);
		var imageWriter = new ImageWriter();
		imageWriter.WritePng(data, width, height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
	}

	private byte[] CreatePixelData(int width, int height, byte[] color)
	{
		var data = new byte[width * height * 4];
		for (var i = 0; i < width * height; ++i)
		{
			Array.Copy(color, 0, data, i * 4, 4);
		}

		return data;
	}

	private void SetPixel(byte[] data, int width, int x, int y, byte[] color)
	{
		var pos = (y * width + x) * 4;
		Array.Copy(color, 0, data, pos, 4);
	}

	private bool IsPowerOf2(int value)
	{
		return value > 0 && (value & (value - 1)) == 0;
	}

	private void AssertRegionAttributes(XElement region)
	{
		var id = region.Attribute("Id");
		var left = region.Attribute("Left");
		var top = region.Attribute("Top");
		var width = region.Attribute("Width");
		var height = region.Attribute("Height");

		Assert.NotNull(id);
		Assert.NotNull(left);
		Assert.NotNull(top);
		Assert.NotNull(width);
		Assert.NotNull(height);

		Assert.False(string.IsNullOrEmpty(id.Value));
		Assert.True(int.TryParse(left.Value, out _));
		Assert.True(int.TryParse(top.Value, out _));
		Assert.True(int.TryParse(width.Value, out _));
		Assert.True(int.TryParse(height.Value, out _));
	}

	private void AssertNinePatchAttributes(XElement region)
	{
		var left = region.Attribute("NinePatchLeft");
		var top = region.Attribute("NinePatchTop");
		var right = region.Attribute("NinePatchRight");
		var bottom = region.Attribute("NinePatchBottom");

		Assert.NotNull(left);
		Assert.NotNull(top);
		Assert.NotNull(right);
		Assert.NotNull(bottom);

		Assert.True(int.TryParse(left.Value, out _));
		Assert.True(int.TryParse(top.Value, out _));
		Assert.True(int.TryParse(right.Value, out _));
		Assert.True(int.TryParse(bottom.Value, out _));
	}
}
