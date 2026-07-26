using Xunit;
using System.Linq;

namespace CodeMap.Test
{
    public class TsTests
    {
        [Fact]
        public void MapperDebug_UserCard()
        {
            var code = @"const UserCard: React.FC<UserCardProps> = ({ user, onSelect }: UserCardProps) => {
  return (
    <article style={cardStyle} onClick={() => onSelect?.(user)}>
      <h3 style={{ margin: 0 }}>{user.name}</h3>
      <p style={{ margin: '4px 0 0 0', fontSize: 12, color: '#444' }}>{user.email ?? 'No email'}</p>
      <small style={{ color: '#666' }}>Joined: {formatDate(user.joined)}</small>
    </article>
  );
};";

            var codeLines = code.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
            var map = TypeScriptMapper.Generate(codeLines, true);

            var items = map.Select(x => x).Concat(map.SelectMany(x => x.Children)).ToArray();

            var outDir = "TestArtifacts";
            System.IO.Directory.CreateDirectory(outDir);
            var outPath = System.IO.Path.Combine(outDir, "UserCardMap.txt");
            using (var w = System.IO.File.CreateText(outPath))
            {
                foreach (var it in items)
                {
                    w.WriteLine($"Line:{it.Line} Type:{it.MemberType} Parent:{it.ParentPath} Name:{it.Name} Content:{it.Content}");
                }
            }

            Assert.NotEmpty(items);
        }
    }
}
