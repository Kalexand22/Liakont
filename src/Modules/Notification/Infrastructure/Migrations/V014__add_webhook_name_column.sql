ALTER TABLE notification.webhook_subscriptions
    ADD COLUMN name text NOT NULL DEFAULT '';

UPDATE notification.webhook_subscriptions
    SET name = 'Webhook ' || substr(id::text, 1, 8)
    WHERE name = '';

ALTER TABLE notification.webhook_subscriptions
    ALTER COLUMN name DROP DEFAULT;
