using Microsoft.EntityFrameworkCore;
using RedNote.ContentService.Domain.Posts;
using RedNote.ContentService.Domain.Users;

namespace RedNote.ContentService.Infrastructure.Persistence;

public sealed class ContentServiceDbContext(
    DbContextOptions<ContentServiceDbContext> options)
    : DbContext(options)
{
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostMedia> PostMedia => Set<PostMedia>();
    public DbSet<PostLike> PostLikes => Set<PostLike>();
    public DbSet<PostFavorite> PostFavorites => Set<PostFavorite>();
    public DbSet<PostTag> PostTags => Set<PostTag>();
    public DbSet<PostComment> PostComments => Set<PostComment>();

    public DbSet<UserProfileProjection> UserProfileProjections => Set<UserProfileProjection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PostTag>(entity =>
        {
            entity.ToTable("PostTags");

            entity.HasKey(postTag => new
            {
                postTag.PostId,
                postTag.Name
            });

            entity.Property(postTag => postTag.Name)
                .HasMaxLength(30)
                .IsRequired();

            entity.HasIndex(postTag => postTag.Name);
            entity.HasIndex(postTag => postTag.PostId);
        });

        modelBuilder.Entity<Post>(entity =>
        {
            entity.ToTable("Posts");

            entity.HasKey(post => post.Id);

            entity.Property(post => post.Title)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(post => post.Content)
                .HasMaxLength(5000)
                .IsRequired();

            entity.Property(post => post.Status)
                .IsRequired();

            entity.Property(post => post.CreatedAtUtc)
                .IsRequired();

            entity.Property(post => post.UpdatedAtUtc)
                .IsRequired();

            entity.HasIndex(post => new
            {
                post.AuthorUserId,
                post.CreatedAtUtc
            });
        });

        modelBuilder.Entity<PostMedia>(entity =>
        {
            entity.ToTable("PostMedia");

            entity.HasKey(postMedia => new
            {
                postMedia.PostId,
                postMedia.MediaId
            });

            entity.Property(postMedia => postMedia.SortOrder)
                .IsRequired();

            entity.HasIndex(postMedia => new
            {
                postMedia.PostId,
                postMedia.SortOrder
            });
        });

        modelBuilder.Entity<PostLike>(entity =>
        {
            entity.ToTable("PostLikes");

            entity.HasKey(postLike => new
            {
                postLike.PostId,
                postLike.UserId
            });

            entity.Property(postLike => postLike.CreatedAtUtc)
                .IsRequired();

            entity.HasIndex(postLike => postLike.UserId);

            entity.HasIndex(postLike => new
            {
                postLike.PostId,
                postLike.CreatedAtUtc
            });
        });

        modelBuilder.Entity<PostFavorite>(entity =>
        {
            entity.ToTable("PostFavorites");

            entity.HasKey(favorite => new
            {
                favorite.PostId,
                favorite.UserId
            });

            entity.Property(favorite => favorite.CreatedAtUtc)
                .IsRequired();

            entity.HasIndex(favorite => favorite.UserId);

            entity.HasIndex(favorite => new
            {
                favorite.UserId,
                favorite.CreatedAtUtc
            });
        });

        modelBuilder.Entity<PostComment>(entity =>
        {
            entity.ToTable("PostComments");

            entity.HasKey(comment => comment.Id);

            entity.Property(comment => comment.Content)
                .HasMaxLength(1000)
                .IsRequired();

            entity.Property(comment => comment.Status)
                .IsRequired();

            entity.Property(comment => comment.CreatedAtUtc)
                .IsRequired();

            entity.Property(comment => comment.UpdatedAtUtc)
                .IsRequired();

            entity.HasIndex(comment => new
            {
                comment.PostId,
                comment.CreatedAtUtc
            });

            entity.HasIndex(comment => new
            {
                comment.ParentCommentId,
                comment.CreatedAtUtc
            });

            entity.HasIndex(comment => new
            {
                comment.AuthorUserId,
                comment.CreatedAtUtc
            });
        });

        /*
         * UserService 用户展示资料的本地读模型。
         *
         * ContentService 只读取它，不拥有用户资料业务规则。
         */
        modelBuilder.Entity<UserProfileProjection>(entity =>
        {
            entity.ToTable("UserProfileProjections");

            entity.HasKey(profile => profile.UserId);

            entity.Property(profile => profile.UserId)
                .ValueGeneratedNever();

            entity.Property(profile => profile.Nickname)
                .HasMaxLength(64);

            entity.Property(profile => profile.AvatarUrl)
                .HasMaxLength(2_048);

            entity.Property(profile => profile.UpdatedAtUtc)
                .IsRequired();
        });
    }
}