using System.Collections.Generic;
using ISIDA.Common;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Тесты валидации файла образов действий с 9-м столбцом Params
  /// (<see cref="FileValidator.IsValidActionsImagesFile(IEnumerable{string})"/>).
  /// </summary>
  public class ActionsImagesParamsValidatorTests
  {
    // Формат строки данных: ID|ActIdList|PhraseIdList|ToneID|MoodID|Kind|VisualColorID|CommandPatternIdList|Params

    [Fact]
    public void IsValidActionsImagesFile_SixColumns_Legacy_True()
    {
      // Старый формат: 6 столбцов (без VisualColorID, CommandPatternIdList, Params)
      var lines = new List<string>
      {
        FileValidator.FileHeaders.ActionsImagesFormat,
        "1|10,11||0|0|0"
      };
      Assert.True(FileValidator.IsValidActionsImagesFile(lines));
    }

    [Fact]
    public void IsValidActionsImagesFile_EightColumns_NoParams_True()
    {
      // Формат до Params: 8 столбцов
      var lines = new List<string>
      {
        FileValidator.FileHeaders.ActionsImagesFormat,
        "1|10,11||0|0|0|0|"
      };
      Assert.True(FileValidator.IsValidActionsImagesFile(lines));
    }

    [Fact]
    public void IsValidActionsImagesFile_NineColumns_ValidParams_True()
    {
      var lines = new List<string>
      {
        FileValidator.FileHeaders.ActionsImagesFormat,
        "1|10,11||0|0|0|0||width=120;height=80"
      };
      Assert.True(FileValidator.IsValidActionsImagesFile(lines));
    }

    [Fact]
    public void IsValidActionsImagesFile_NineColumns_EmptyParams_True()
    {
      var lines = new List<string>
      {
        FileValidator.FileHeaders.ActionsImagesFormat,
        "1|10,11||0|0|0|0||"
      };
      Assert.True(FileValidator.IsValidActionsImagesFile(lines));
    }

    [Fact]
    public void IsValidActionsImagesFile_InvalidParams_NoEquals_False()
    {
      // Параметры без ключа («=value») — некорректно
      var lines = new List<string>
      {
        FileValidator.FileHeaders.ActionsImagesFormat,
        "1|10,11||0|0|0|0||=value"
      };
      Assert.False(FileValidator.IsValidActionsImagesFile(lines));
    }

    [Fact]
    public void IsValidActionsImagesFile_InvalidParams_NakedToken_False()
    {
      // Токен без «=» — некорректно
      var lines = new List<string>
      {
        FileValidator.FileHeaders.ActionsImagesFormat,
        "1|10,11||0|0|0|0||naked"
      };
      Assert.False(FileValidator.IsValidActionsImagesFile(lines));
    }

    [Fact]
    public void IsValidActionsImagesFile_ParamsValueWithEquals_True()
    {
      // Значение может содержать «=» (разделяется по первому)
      var lines = new List<string>
      {
        FileValidator.FileHeaders.ActionsImagesFormat,
        "1|10,11||0|0|0|0||formula=a=b"
      };
      Assert.True(FileValidator.IsValidActionsImagesFile(lines));
    }

    [Fact]
    public void IsValidActionsImagesFile_OnlyHeader_True()
    {
      Assert.True(FileValidator.IsValidActionsImagesFile(
          new List<string> { FileValidator.FileHeaders.ActionsImagesFormat }));
    }

    [Fact]
    public void IsValidActionsImagesFile_NullOrEmpty_False()
    {
      Assert.False(FileValidator.IsValidActionsImagesFile((IEnumerable<string>)null));
      Assert.False(FileValidator.IsValidActionsImagesFile(new List<string>()));
    }

    [Fact]
    public void IsValidActionsImagesFile_ZeroId_False()
    {
      var lines = new List<string>
      {
        FileValidator.FileHeaders.ActionsImagesFormat,
        "0|10,11||0|0|0|0||a=1"
      };
      Assert.False(FileValidator.IsValidActionsImagesFile(lines));
    }
  }
}
