using System.Collections.Generic;

namespace ISIDA.Common
{
  /// <summary>
  /// Узел дерева шаблона каталогов проекта данных ISIDA (только для отображения в интерфейсе).
  /// </summary>
  public sealed class ProjectDirectoryTemplateNode
  {
    /// <summary>
    /// Создаёт узел с необязательными дочерними узлами.
    /// </summary>
    /// <param name="name">Подпись узла (имя каталога или файла).</param>
    /// <param name="children">Дочерние узлы; если null — узел считается листом.</param>
    public ProjectDirectoryTemplateNode(string name, IList<ProjectDirectoryTemplateNode> children = null)
    {
      Name = name ?? string.Empty;
      Children = children ?? new List<ProjectDirectoryTemplateNode>();
    }

    /// <summary>Подпись узла (имя каталога или файла).</summary>
    public string Name { get; }

    /// <summary>Вложенные каталоги и файлы.</summary>
    public IList<ProjectDirectoryTemplateNode> Children { get; }
  }
}
