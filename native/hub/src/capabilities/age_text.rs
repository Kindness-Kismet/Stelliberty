use std::io::{Cursor, Read};

use age::{Decryptor, Identity, IdentityFile, armor::ArmoredReader};
use anyhow::{Context, Result, anyhow};
use interoptopus::ffi;

const AGE_ARMOR_HEADER: &str = "-----BEGIN AGE ENCRYPTED FILE-----";

pub fn decrypt_text(content: &str, age_secret_key: &str) -> Result<String> {
    if age_secret_key.trim().is_empty() || !is_age_armor(content) {
        return Ok(content.to_string());
    }

    let identity_file = IdentityFile::from_buffer(Cursor::new(age_secret_key.trim().as_bytes()))
        .context("Invalid age secret key")?;
    let identities = identity_file
        .into_identities()
        .context("Invalid age identity")?;
    let decryptor = Decryptor::new_buffered(ArmoredReader::new(content.as_bytes()))
        .context("Invalid age encrypted content")?;
    let mut reader = decryptor
        .decrypt(
            identities
                .iter()
                .map(|identity| identity.as_ref() as &dyn Identity),
        )
        .map_err(|err| anyhow!("Failed to decrypt age encrypted content: {err}"))?;

    let mut plaintext = Vec::new();
    reader
        .read_to_end(&mut plaintext)
        .context("Failed to read decrypted age content")?;
    String::from_utf8(plaintext).context("Decrypted age content is not valid UTF-8")
}

fn is_age_armor(content: &str) -> bool {
    content
        .trim_start_matches(['\u{feff}', '\r', '\n', '\t', ' '])
        .starts_with(AGE_ARMOR_HEADER)
}

#[ffi]
pub fn hub_age_text_decrypt(content: ffi::String, age_secret_key: ffi::String) -> ffi::String {
    match decrypt_text(content.as_str(), age_secret_key.as_str()) {
        Ok(out) => ffi::String::from_string(out),
        Err(err) => ffi::String::from_string(format!("ERR:{err:#}")),
    }
}
