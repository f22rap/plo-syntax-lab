import {sqliteTable,text,integer,primaryKey,index,uniqueIndex} from 'drizzle-orm/sqlite-core';
export const records=sqliteTable('plo_records',{
 owner:text('owner').notNull(), id:text('id').notNull(),kind:text('kind').notNull(),
 data:text('data').notNull(),createdAt:integer('created_at').notNull(),expiresAt:integer('expires_at').notNull(),
 bytes:integer('bytes').notNull().default(0),revision:integer('revision').notNull().default(0),
 requestKey:text('request_key'),signature:text('signature'),state:text('state'),retainUntil:integer('retain_until').notNull().default(0)
},table=>[
 primaryKey({columns:[table.owner,table.id]}),
 index('idx_plo_records_owner_kind_expiry').on(table.owner,table.kind,table.expiresAt),
 uniqueIndex('idx_plo_records_owner_request').on(table.owner,table.requestKey)
]);
