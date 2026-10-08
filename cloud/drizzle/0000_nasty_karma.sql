CREATE TABLE `plo_records` (
	`owner` text NOT NULL,
	`id` text NOT NULL,
	`kind` text NOT NULL,
	`data` text NOT NULL,
	`created_at` integer NOT NULL,
	`expires_at` integer NOT NULL,
	`bytes` integer DEFAULT 0 NOT NULL,
	`revision` integer DEFAULT 0 NOT NULL,
	`request_key` text,
	`signature` text,
	`state` text,
	`retain_until` integer DEFAULT 0 NOT NULL,
	PRIMARY KEY(`owner`, `id`)
);
--> statement-breakpoint
CREATE INDEX `idx_plo_records_owner_kind_expiry` ON `plo_records` (`owner`,`kind`,`expires_at`);--> statement-breakpoint
CREATE UNIQUE INDEX `idx_plo_records_owner_request` ON `plo_records` (`owner`,`request_key`);