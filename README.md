# MyraTexturePacker
MyraTexturePacker is a console utility for making a texture atlas.

## Installation

```bash
dotnet tool install --global myratexpack
```

## Update

```bash
dotnet tool update --global myratexpack
```

# Usage
`myratexpack <input_folder> <output_file> [width] [height] [padding]`

E.g.
`myratexpack "C:\Temp" "C:\Temp\my_atlas.png"`

That command will process all images in the folder "C:\Temp" and create a texture atlas from them.

`width` and `height` are optional parameters. If they aren't provided, then default values 256x256 are used.
If the images don't fit on the atlas, then its size is doubled until the images fit.

`padding` is an optional parameter that sets the images' padding in pixels. If it isn't provided, then the default value of 2 is used.

The texture atlas will consist of two files: my_atlas.png (atlas image) and my_atlas.xmat (atlas definition in XML format).

MyraTexturePacker supports nine patch images. In order to use that feature, the input image name must have ".9" before the extension (e.g. `image.9.png`). Also, such an image must have a 1px border with black lines marking stretchable areas. 
See this link for thorough explanation of this feature: https://libgdx.com/wiki/graphics/2d/ninepatches

Example set of input images (both ordinary and nine-patch): https://github.com/MyraUI/Myra/tree/master/assets-raw

# Who Uses It?
https://github.com/MyraUI/Myra
