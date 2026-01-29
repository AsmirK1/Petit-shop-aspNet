using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Petit_shope_Asp_backend.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Use raw SQL with IF NOT EXISTS to avoid errors if columns already exist
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                                   WHERE table_name='Users' AND column_name='EmailVerificationExpires') THEN
                        ALTER TABLE ""Users"" ADD COLUMN ""EmailVerificationExpires"" timestamp with time zone;
                    END IF;
                    
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                                   WHERE table_name='Users' AND column_name='EmailVerificationToken') THEN
                        ALTER TABLE ""Users"" ADD COLUMN ""EmailVerificationToken"" text;
                    END IF;
                    
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns 
                                   WHERE table_name='Users' AND column_name='EmailVerified') THEN
                        ALTER TABLE ""Users"" ADD COLUMN ""EmailVerified"" boolean NOT NULL DEFAULT false;
                    END IF;
                END
                $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Use raw SQL to safely drop columns
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns 
                               WHERE table_name='Users' AND column_name='EmailVerificationExpires') THEN
                        ALTER TABLE ""Users"" DROP COLUMN ""EmailVerificationExpires"";
                    END IF;
                    
                    IF EXISTS (SELECT 1 FROM information_schema.columns 
                               WHERE table_name='Users' AND column_name='EmailVerificationToken') THEN
                        ALTER TABLE ""Users"" DROP COLUMN ""EmailVerificationToken"";
                    END IF;
                    
                    IF EXISTS (SELECT 1 FROM information_schema.columns 
                               WHERE table_name='Users' AND column_name='EmailVerified') THEN
                        ALTER TABLE ""Users"" DROP COLUMN ""EmailVerified"";
                    END IF;
                END
                $$;
            ");
        }
    }
}
