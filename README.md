# Unity TextMeshPro Font Customized Creater

## 原因
Unity 推荐使用 TextMesh Pro 来代替内置的现有文本组件如 Text Mesh 及 UI Text，因为 TextMesh Pro 可以渲染出非常清晰的文本。在使用过程中发现，发现有以下问题：

1. TextMesh Pro 需要先生成一张静态字体图集，每当静态文字增加时，都需要打开自带的生成工具，一个参数一个参数的设置，非常麻烦，也不便于其他人员使用。

1. 每一个字体资产都会生成一个图集，这对于相同字体，但是不同样式的字体资产就会冗余字体图集。


1. 一个字体里通常不会包含所有的字符，当遇到缺失字符时，就会空缺显示，如果采用回调字体方式，又会增加 Draw Call，最好是可以采用备用字体生成在同一个图集里。


1. 某些字符在首位字体里并不美观，需要可以指定使用某个备用字体来渲染。


## 目标

这个工具要解决这些问题，达成以下目标：

1. 一键式生成预设字体图集

1. 相同字体复用同一个图集

1. 字符缺失的采用备用字体

1. 指定字符只采用某个字体

## 示意图

![](https://github.com/akof1314/UnityTMProFontCustomizedCreater/raw/master/Pic/pic.png)