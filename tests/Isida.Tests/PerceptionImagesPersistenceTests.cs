using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Персистентность образов восприятия (<c>PerceptionImages.dat</c>): дедупликация при добавлении,
  /// корректность round-trip «сохранить → выгрузить из памяти → загрузить», разбор legacy-формата
  /// (4-полевая строка без <c>CommandPatternIdList</c>).
  /// <para>
  /// Тесты поднимаются поверх живого движка (<see cref="EngineIntegrationCollection"/>), но не трогают
  /// условные рефлексы: работают только с <see cref="PerceptionImagesSystem"/> и его файлом.
  /// Приватные <c>LoadPerceptionImages</c>/<c>LoadBehaviorStyleImages</c> вызываются через рефлексию —
  /// это тот же путь, что конструктор системы при старте движка.
  /// </para>
  /// </summary>
  [Collection("EngineIntegration")]
  public sealed class PerceptionImagesPersistenceTests : EngineIntegrationTestBase
  {
    public PerceptionImagesPersistenceTests(EngineFixture engine) : base(engine) { }

    private static readonly MethodInfo LoadPerceptionImages =
        typeof(PerceptionImagesSystem).GetMethod("LoadPerceptionImages",
            BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo LoadBehaviorStyleImages =
        typeof(PerceptionImagesSystem).GetMethod("LoadBehaviorStyleImages",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private static void ReloadPerceptionImages() =>
        LoadPerceptionImages.Invoke(Images, null);

    /// <summary>Путь к PerceptionImages.dat в каталоге данных движка фикстуры.</summary>
    private string PerceptionImagesFile()
    {
      string reflexesPath = GeneticReflexesSystem.Instance.GetGeneticReflexesFilePath();
      return Path.Combine(Path.GetDirectoryName(reflexesPath), "PerceptionImages.dat");
    }

    // ---------- Дедупликация командного канала ----------

    [Fact]
    public void AddPerceptionImage_CommandPatternOrderIndependent_SameId()
    {
      // Регрессия: раньше CommandPatternIdList сравнивался без нормализации порядка,
      // и [3,1] vs [1,3] давали два разных образа (дубликат с новым ID).
      int id1 = Images.AddPerceptionImage(new List<int>(), new List<int>(), 0, new List<int> { 3, 1 });
      int id2 = Images.AddPerceptionImage(new List<int>(), new List<int>(), 0, new List<int> { 1, 3 });

      Assert.True(id1 > 0);
      Assert.Equal(id1, id2);
    }

    [Fact]
    public void AddPerceptionImage_DifferentCommandSet_DifferentId()
    {
      int id1 = Images.AddPerceptionImage(new List<int>(), new List<int>(), 0, new List<int> { 1, 3 });
      int id2 = Images.AddPerceptionImage(new List<int>(), new List<int>(), 0, new List<int> { 1, 4 });

      Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void AddPerceptionImage_CommandsStoredSorted()
    {
      int id = Images.AddPerceptionImage(new List<int>(), new List<int>(), 0, new List<int> { 9, 2, 7 });
      var img = Images.GetAllPerceptionImagesList().Single(x => x.Id == id);

      Assert.Equal(new List<int> { 2, 7, 9 }, img.CommandPatternIdList);
    }

    // ---------- Round-trip «сохранить → загрузить» ----------

    [Fact]
    public void RoundTrip_PreservesIdsAndContent()
    {
      int actionImg = Images.AddPerceptionImage(new List<int> { 11, 12 }, new List<int>(), 0, new List<int> { 5, 1 });
      int phraseImg = Images.AddPerceptionImage(new List<int>(), new List<int> { 21 }, 3, new List<int>());
      int multiImg = Images.AddPerceptionImage(new List<int> { 31 }, new List<int> { 41 }, 7, new List<int> { 2, 8 });
      int styleImg = Images.AddBehaviorStyleImage(new List<int> { 100, 101 });

      Assert.True(Images.SaveAllImages().Success);

      // Выгружаем память и грузим обратно строго с диска.
      Images.ClearAllPerceptionImages();
      Images.ClearAllBehaviorStyleImages();
      Assert.Empty(Images.GetAllPerceptionImagesList());

      ReloadPerceptionImages();
      LoadBehaviorStyleImages.Invoke(Images, null);

      var byId = Images.GetAllPerceptionImagesList().ToDictionary(x => x.Id);
      Assert.True(byId.ContainsKey(actionImg), "образ действий должен восстановиться с тем же ID");
      Assert.Equal(new List<int> { 11, 12 }, byId[actionImg].InfluenceActionsList);
      Assert.Equal(new List<int> { 1, 5 }, byId[actionImg].CommandPatternIdList);
      Assert.Equal(new List<int> { 21 }, byId[phraseImg].PhraseIdList);
      Assert.Equal(3, byId[phraseImg].VisualColorId);
      Assert.Equal(7, byId[multiImg].VisualColorId);
      Assert.Equal(new List<int> { 2, 8 }, byId[multiImg].CommandPatternIdList);

      var style = Images.GetAllBehaviorStyleImagesList().Single(x => x.Id == styleImg);
      Assert.Equal(new List<int> { 100, 101 }, style.BehaviorStylesList);
    }

    [Fact]
    public void RoundTrip_DeduplicatesOnReload_NoIdDrift()
    {
      // Один и тот же стимул добавлен дважды — в памяти один ID.
      int id = Images.AddPerceptionImage(new List<int> { 1 }, new List<int> { 2 }, 0, new List<int> { 3 });
      int dup = Images.AddPerceptionImage(new List<int> { 1 }, new List<int> { 2 }, 0, new List<int> { 3 });
      Assert.Equal(id, dup);

      Assert.True(Images.SaveAllImages().Success);
      Images.ClearAllPerceptionImages();
      ReloadPerceptionImages();

      // После перезагрузки — ровно один образ, и он тот же по содержимому.
      var images = Images.GetAllPerceptionImagesList();
      Assert.Single(images);
      Assert.Equal(id, images[0].Id);
      Assert.True(PerceptionImagesSystem.PerceptionImagesEqual(
          images[0],
          new PerceptionImagesSystem.PerceptionImage
          {
            InfluenceActionsList = new List<int> { 1 },
            PhraseIdList = new List<int> { 2 },
            CommandPatternIdList = new List<int> { 3 },
            VisualColorId = 0
          }));
    }

    // ---------- Legacy-формат файла ----------

    [Fact]
    public void Load_LegacyFourFieldRow_ReadsColorAndEmptyCommands()
    {
      // Старый формат: ID|InfluenceActionsList|PhraseIdList|VisualColorId (без CommandPatternIdList).
      WritePerceptionImagesFile(
          "# header",
          "1|10,11|20|4",   // цвет=4, команд нет
          "2||30|1");        // только фраза, цвет=чёрный

      ReloadPerceptionImages();

      var byId = Images.GetAllPerceptionImagesList().ToDictionary(x => x.Id);
      Assert.Equal(new List<int> { 10, 11 }, byId[1].InfluenceActionsList);
      Assert.Equal(new List<int> { 20 }, byId[1].PhraseIdList);
      Assert.Empty(byId[1].CommandPatternIdList);
      Assert.Equal(4, byId[1].VisualColorId);

      Assert.Empty(byId[2].InfluenceActionsList);
      Assert.Equal(new List<int> { 30 }, byId[2].PhraseIdList);
      Assert.Equal(1, byId[2].VisualColorId);
    }

    [Fact]
    public void Load_ModernFiveFieldRow_SortsCommands()
    {
      // Новый формат: ID|actions|phrases|CommandPatternIdList|VisualColorId.
      // Команды в файле намеренно не отсортированы — при загрузке нормализуются.
      WritePerceptionImagesFile(
          "# header",
          "1|10||6,5|2");

      ReloadPerceptionImages();

      var img = Images.GetAllPerceptionImagesList().Single(x => x.Id == 1);
      Assert.Equal(new List<int> { 5, 6 }, img.CommandPatternIdList);
      Assert.Equal(2, img.VisualColorId);
    }

    [Fact]
    public void Load_LegacyRow_ThenAddSortedCommands_NoDuplicate()
    {
      // Legacy-файл с несортированными командами (мог быть записан до фикса) после загрузки
      // даёт канонический порядок, поэтому повторное добавление того же стимула — не дубликат.
      WritePerceptionImagesFile("# header", "1|10||9,2|0");
      ReloadPerceptionImages();

      int loadedId = Images.GetAllPerceptionImagesList().Single().Id;
      int again = Images.AddPerceptionImage(new List<int> { 10 }, new List<int>(), 0, new List<int> { 2, 9 });

      Assert.Equal(loadedId, again);
      Assert.Single(Images.GetAllPerceptionImagesList());
    }

    /// <summary>Перезаписывает PerceptionImages.dat указанными строками (шапку передаёт вызывающий).</summary>
    private void WritePerceptionImagesFile(params string[] lines)
    {
      File.WriteAllLines(PerceptionImagesFile(), lines);
    }
  }
}
