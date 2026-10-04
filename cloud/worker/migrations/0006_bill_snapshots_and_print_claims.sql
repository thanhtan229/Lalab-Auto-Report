ALTER TABLE cloud_bills ADD COLUMN product_subtotal INTEGER;
ALTER TABLE cloud_bills ADD COLUMN adjustments_total INTEGER;
ALTER TABLE cloud_print_commands ADD COLUMN claim_token TEXT;
