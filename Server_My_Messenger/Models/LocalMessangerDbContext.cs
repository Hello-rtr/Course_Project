using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Server_My_Messenger.Models;

public partial class LocalMessangerDbContext : DbContext
{
    public LocalMessangerDbContext()
    {
    }

    public LocalMessangerDbContext(DbContextOptions<LocalMessangerDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Chat> Chats { get; set; }
    public virtual DbSet<ChatType> ChatTypes { get; set; }
    public virtual DbSet<Message> Messages { get; set; }
    public virtual DbSet<MessageUserInChat> MessageUserInChats { get; set; }
    public virtual DbSet<User> Users { get; set; }
    public virtual DbSet<Statuses> Statuses { get; set; } // Добавить
    public virtual DbSet<UserInChat> UserInChats { get; set; } // Добавить
    public virtual DbSet<UserRole> UserRoles { get; set; } // Добавить

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseSqlServer("Server=HELLO\\HELLOSQL;Database=LocalMessangerDB;Trusted_Connection=True;TrustServerCertificate=true;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Chat>(entity =>
        {
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name)
                .HasMaxLength(20)
                .IsUnicode(false);

            entity.HasOne(d => d.ChatType).WithMany(p => p.Chats)
                .HasForeignKey(d => d.ChatTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Chats_ChatTypes");
        });

        modelBuilder.Entity<ChatType>(entity =>
        {
            entity.Property(e => e.Description)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Name)
                .HasMaxLength(20)
                .IsUnicode(false);
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Data)
                .HasMaxLength(4096)
                .IsUnicode(false);
        });

        modelBuilder.Entity<MessageUserInChat>(entity =>
        {
            entity.HasKey(e => new { e.IdMessage, e.IdChat, e.IdUser });

            entity.ToTable("MessageUserInChat");

            entity.HasOne(d => d.IdChatNavigation).WithMany(p => p.MessageUserInChats)
                .HasForeignKey(d => d.IdChat)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MessageUserInChat_Chats");

            entity.HasOne(d => d.IdMessageNavigation).WithMany(p => p.MessageUserInChats)
                .HasForeignKey(d => d.IdMessage)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MessageUserInChat_Messages");

            entity.HasOne(d => d.IdUserNavigation).WithMany(p => p.MessageUserInChats)
                .HasForeignKey(d => d.IdUser)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MessageUserInChat_User");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_User");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Login)
                .HasMaxLength(15)
                .IsUnicode(false);
            entity.Property(e => e.Name)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.Password)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.SecondSurname)
                .HasMaxLength(20)
                .IsUnicode(false);
            entity.Property(e => e.Surname)
                .HasMaxLength(20)
                .IsUnicode(false);

            entity.HasOne(d => d.Status).WithMany(p => p.Users)
                .HasForeignKey(d => d.StatusId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_User_Status"); // Добавить
        });

        modelBuilder.Entity<UserInChat>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.ChatId });

            entity.ToTable("UserInChat");

            entity.Property(e => e.UserId).HasColumnName("UserId");
            entity.Property(e => e.ChatId).HasColumnName("ChatId");
            entity.Property(e => e.RoleId).HasColumnName("RoleId");
            entity.Property(e => e.DateOfEntry).HasColumnType("datetime");
            entity.Property(e => e.DateRelease).HasColumnType("datetime");

            entity.HasOne(d => d.Chat).WithMany(p => p.UserInChats)
                .HasForeignKey(d => d.ChatId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserInChat_Chats");

            entity.HasOne(d => d.Role).WithMany(p => p.UserInChats)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserInChat_UserRoles");

            entity.HasOne(d => d.User).WithMany(p => p.UserInChats)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserInChat_Users");
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Role)
                .HasMaxLength(50)
                .IsUnicode(false);
        });

        modelBuilder.Entity<Statuses>(entity =>
        {
            entity.ToTable("Statuses"); 
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .IsUnicode(false);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}