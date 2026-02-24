-- Emergency script to restore SuperAdmin access
-- Run this on Railway PostgreSQL console if you're locked out

-- Replace 'your.email@example.com' with your actual email address
UPDATE "TeamMembers" 
SET "Role" = 'SuperAdmin'
WHERE "Email" ILIKE 'your.email@example.com';

-- Verify the change
SELECT "Id", "Email", "DisplayName", "Role", "Status" 
FROM "TeamMembers" 
WHERE "Email" ILIKE 'your.email@example.com';
